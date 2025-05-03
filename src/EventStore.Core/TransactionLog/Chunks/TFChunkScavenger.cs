using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using EventStore.Core.Exceptions;
using EventStore.Core.Index;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.LogCommon;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.TransactionLog.Chunks {
	public abstract class TFChunkScavenger {
		public const int MaxThreadCount = 4;
		public const int MaxRetryCount = 5;
		public const int FlushPageInterval = 32; // max 65536 pages to write resulting in 2048 flushes per chunk
	}

	public class TFChunkScavenger<TStreamId> : TFChunkScavenger {
		private readonly ILogger _logger;
		private readonly TFChunkDb _db;
		private readonly ITFChunkScavengerLog _scavengerLog;
		private readonly ITableIndex<TStreamId> _tableIndex;
		private readonly IReadIndex<TStreamId> _readIndex;
		private readonly IMetastreamLookup<TStreamId> _metastreams;
		private readonly long _maxChunkDataSize;
		private readonly bool _unsafeIgnoreHardDeletes;
		private readonly int _threads;

		public TFChunkScavenger(ILogger logger, TFChunkDb db, ITFChunkScavengerLog scavengerLog, ITableIndex<TStreamId> tableIndex,
			IReadIndex<TStreamId> readIndex, IMetastreamLookup<TStreamId> metastreams, long? maxChunkDataSize = null,
			bool unsafeIgnoreHardDeletes = false, int threads = 1) {
			Ensure.NotNull(logger, nameof(logger));
			Ensure.NotNull(db, "db");
			Ensure.NotNull(scavengerLog, "scavengerLog");
			Ensure.NotNull(tableIndex, "tableIndex");
			Ensure.NotNull(readIndex, "readIndex");
			Ensure.NotNull(metastreams, nameof(metastreams));
			Ensure.Positive(threads, "threads");

			_logger = logger;

			if (threads > MaxThreadCount) {
				_logger.Warning(
					"{numThreads} scavenging threads not allowed.  Max threads allowed for scavenging is {maxThreadCount}. Capping.",
					threads, MaxThreadCount);
				threads = MaxThreadCount;
			}

			_db = db;
			_scavengerLog = scavengerLog;
			_tableIndex = tableIndex;
			_readIndex = readIndex;
			_metastreams = metastreams;
			_maxChunkDataSize = maxChunkDataSize ?? db.Config.ChunkSize;
			_unsafeIgnoreHardDeletes = unsafeIgnoreHardDeletes;
			_threads = threads;
		}

		public string ScavengeId => _scavengerLog.ScavengeId;

		private static IEnumerable<TFChunk.TFChunk> GetAllChunks(TFChunkDb db, int startFromChunk) {
			long scavengePos = db.Config.ChunkSize * (long)startFromChunk;
			while (scavengePos < db.Config.ChaserCheckpoint.Read()) {
				var chunk = db.Manager.GetChunkFor(scavengePos);
				if (!chunk.IsReadOnly) {
					yield break;
				}

				yield return chunk;

				scavengePos = chunk.ChunkHeader.ChunkEndPosition;
			}
		}

		public Task Scavenge(bool alwaysKeepScavenged, bool mergeChunks, int startFromChunk = 0,
			bool scavengeIndex = true,
			CancellationToken ct = default(CancellationToken)) {
			Ensure.Nonnegative(startFromChunk, nameof(startFromChunk));

			// Note we aren't passing the CancellationToken to the task on purpose so awaiters
			// don't have to handle Exceptions and can wait for the actual completion of the task.
			return Task.Factory.StartNew(() => {
				var sw = Stopwatch.StartNew();

				ScavengeResult result = ScavengeResult.Success;
				string error = null;
				try {
					_scavengerLog.ScavengeStarted(alwaysKeepScavenged, mergeChunks, startFromChunk, _threads);

					ScavengeInternal(alwaysKeepScavenged, mergeChunks, startFromChunk, ct);

					if (scavengeIndex) {
						_tableIndex.Scavenge(_scavengerLog, ct);
					}
				} catch (OperationCanceledException) {
					_logger.Information("SCAVENGING: Scavenge cancelled.");
					result = ScavengeResult.Stopped;
				} catch (Exception exc) {
					result = ScavengeResult.Failed;
					_logger.Error(exc, "SCAVENGING: Error while scavenging DB.");
					error = string.Format("Error while scavenging DB: {0}.", exc.Message);
				} finally {
					try {
						_scavengerLog.ScavengeCompleted(result, error, sw.Elapsed);
					} catch (Exception ex) {
						_logger.Error(ex,
							"Error whilst recording scavenge completed. Scavenge result: {result}, Elapsed: {elapsed}, Original error: {e}",
							result, sw.Elapsed, error);
					}
				}
			}, TaskCreationOptions.LongRunning);
		}

		private void ScavengeInternal(bool alwaysKeepScavenged, bool mergeChunks, int startFromChunk,
			CancellationToken ct) {
			var totalSw = Stopwatch.StartNew();
			var sw = Stopwatch.StartNew();

			_logger.Debug(
				"SCAVENGING: Started scavenging of DB. Chunks count at start: {chunksCount}. Options: alwaysKeepScavenged = {alwaysKeepScavenged}, mergeChunks = {mergeChunks}",
				_db.Manager.ChunksCount, alwaysKeepScavenged, mergeChunks);

			// Initial scavenge pass
			var chunksToScavenge = GetAllChunks(_db, startFromChunk);

			using (var scavengeCacheObjectPool = CreateThreadLocalScavengeCachePool(_threads)) {
				Parallel.ForEach(chunksToScavenge,
					new ParallelOptions {MaxDegreeOfParallelism = _threads, CancellationToken = ct},
					(chunk, pls) => {
						var cache = scavengeCacheObjectPool.Get();
						try {
							ScavengeChunk(alwaysKeepScavenged, chunk, cache, ct);
						} finally {
							cache.Reset(); // reset thread local cache before next iteration.
							scavengeCacheObjectPool.Return(cache);
						}
					});
			}

			_logger.Debug("SCAVENGING: Initial pass completed in {elapsed}.", sw.Elapsed);

			// Merge scavenge pass
			if (mergeChunks) {
				MergePhase(
					logger: _logger,
					db: _db,
					maxChunkDataSize: _maxChunkDataSize,
					scavengerLog: _scavengerLog,
					throttle: new Throttle(_logger, TimeSpan.Zero, TimeSpan.Zero, 100),
					ct: ct);
			}

			_logger.Debug("SCAVENGING: Total time taken: {elapsed}, total space saved: {spaceSaved}.", totalSw.Elapsed,
				_scavengerLog.SpaceSaved);
		}

		private void ScavengeChunk(bool alwaysKeepScavenged, TFChunk.TFChunk oldChunk,
			ThreadLocalScavengeCache threadLocalCache, CancellationToken ct) {
			if (oldChunk == null) throw new ArgumentNullException("oldChunk");

			var sw = Stopwatch.StartNew();

			int chunkStartNumber = oldChunk.ChunkHeader.ChunkStartNumber;
			long chunkStartPos = oldChunk.ChunkHeader.ChunkStartPosition;
			int chunkEndNumber = oldChunk.ChunkHeader.ChunkEndNumber;
			long chunkEndPos = oldChunk.ChunkHeader.ChunkEndPosition;

			var tmpChunkPath = Path.Combine(_db.Config.Path, Guid.NewGuid() + ".scavenge.tmp");
			var oldChunkName = oldChunk.ToString();
			_logger.Debug(
				"SCAVENGING: Started to scavenge chunks: {oldChunkName} {chunkStartNumber} => {chunkEndNumber} ({chunkStartPosition} => {chunkEndPosition})",
				oldChunkName, chunkStartNumber, chunkEndNumber,
				chunkStartPos, chunkEndPos);
			_logger.Debug("Resulting temp chunk file: {tmpChunkPath}.", Path.GetFileName(tmpChunkPath));

			TFChunk.TFChunk newChunk;
			try {
				newChunk = TFChunk.TFChunk.CreateNew(
					_db.Manager.ChunkCacheManager,
					tmpChunkPath,
					_db.Config.ChunkSize,
					chunkStartNumber,
					chunkEndNumber,
					isScavenged: true,
					inMem: _db.Config.InMemDb,
					unbuffered: _db.Config.Unbuffered,
					writethrough: _db.Config.WriteThrough,
					initialReaderCount: _db.Config.InitialReaderCount,
					maxReaderCount: _db.Config.MaxReaderCount,
					reduceFileCachePressure: _db.Config.ReduceFileCachePressure,
					tracker: new TFChunkTracker.NoOp());
			} catch (IOException exc) {
				_logger.Error(exc,
					"IOException during creating new chunk for scavenging purposes. Stopping scavenging process...");
				throw;
			}

			try {
				TraverseChunkBasic(oldChunk, ct,
					result => {
						threadLocalCache.Records.Add(result);

						if (result.LogRecord.RecordType == LogRecordType.Commit) {
							var commit = (CommitLogRecord)result.LogRecord;
							if (commit.TransactionPosition >= chunkStartPos)
								threadLocalCache.Commits.Add(commit.TransactionPosition, new CommitInfo(commit));
						}
					});

				long newSize = 0;
				int filteredCount = 0;

				for (int i = 0; i < threadLocalCache.Records.Count; i++) {
					ct.ThrowIfCancellationRequested();

					var recordReadResult = threadLocalCache.Records[i];
					if (ShouldKeep(recordReadResult, threadLocalCache.Commits, chunkStartPos, chunkEndPos)) {
						newSize += recordReadResult.RecordLength + 2 * sizeof(int);
						filteredCount++;
					} else {
						// We don't need this record any more.
						threadLocalCache.Records[i] = default(CandidateRecord);
					}
				}

				_logger.Debug("Scavenging {oldChunkName} traversed {recordsCount} including {filteredCount}.", oldChunkName,
					threadLocalCache.Records.Count, filteredCount);

				newSize += filteredCount * PosMap.FullSize + ChunkHeader.Size + ChunkFooter.Size;
				if (newChunk.ChunkHeader.Version >= (byte)TFChunk.TFChunk.ChunkVersions.Aligned)
					newSize = TFChunk.TFChunk.GetAlignedSize((int)newSize);

				bool oldVersion = oldChunk.ChunkHeader.Version != TFChunk.TFChunk.CurrentChunkVersion;
				long oldSize = oldChunk.FileSize;

				if (oldSize <= newSize && !alwaysKeepScavenged && !_unsafeIgnoreHardDeletes && !oldVersion) {
					_logger.Debug(
						"Scavenging of chunks:"
						+ "\n{oldChunkName}"
						+ "\ncompleted in {elapsed}."
						+ "\nOld chunks' versions are kept as they are smaller."
						+ "\nOld chunk total size: {oldSize}, scavenged chunk size: {newSize}."
						+ "\nScavenged chunk removed.", oldChunkName, sw.Elapsed, oldSize, newSize);

					newChunk.MarkForDeletion();
					_scavengerLog.ChunksNotScavenged(chunkStartNumber, chunkEndNumber, sw.Elapsed, "");
				} else {
					var positionMapping = new List<PosMap>(filteredCount);

					var lastFlushedPage = -1;
					for (int i = 0; i < threadLocalCache.Records.Count; i++) {
						ct.ThrowIfCancellationRequested();

						// Since we replaced the ones we don't want with `default`, the success flag will only be true on the ones we want to keep.
						var recordReadResult = threadLocalCache.Records[i];

						// Check log record, if not present then assume we can skip. 
						if (recordReadResult.LogRecord != null)
							positionMapping.Add(WriteRecord(newChunk, recordReadResult.LogRecord));

						var currentPage = newChunk.RawWriterPosition / 4096;
						if (currentPage - lastFlushedPage > FlushPageInterval) {
							newChunk.Flush();
							lastFlushedPage = currentPage;
						}
					}

					newChunk.CompleteScavenge(positionMapping);

					if (_unsafeIgnoreHardDeletes) {
						_logger.Debug("Forcing scavenge chunk to be kept even if bigger.");
					}

					if (oldVersion) {
						_logger.Debug("Forcing scavenged chunk to be kept as old chunk is a previous version.");
					}

					var chunk = _db.Manager.SwitchChunk(newChunk, verifyHash: false,
						removeChunksWithGreaterNumbers: false);
					if (chunk != null) {
						_logger.Debug("Scavenging of chunks:"
						          + "\n{oldChunkName}"
						          + "\ncompleted in {elapsed}."
						          + "\nNew chunk: {tmpChunkPath} --> #{chunkStartNumber}-{chunkEndNumber} ({newChunk})."
						          + "\nOld chunk total size: {oldSize}, scavenged chunk size: {newSize}.",
							oldChunkName, sw.Elapsed, Path.GetFileName(tmpChunkPath), chunkStartNumber, chunkEndNumber,
							Path.GetFileName(chunk.FileName), oldSize, newSize);
						var spaceSaved = oldSize - newSize;
						_scavengerLog.ChunksScavenged(chunkStartNumber, chunkEndNumber, sw.Elapsed, spaceSaved);
					} else {
						_logger.Debug("Scavenging of chunks:"
						          + "\n{oldChunkName}"
						          + "\ncompleted in {elapsed}."
						          + "\nBut switching was prevented for new chunk: #{chunkStartNumber}-{chunkEndNumber} ({tmpChunkPath})."
						          + "\nOld chunks total size: {oldSize}, scavenged chunk size: {newSize}.",
							oldChunkName, sw.Elapsed, chunkStartNumber, chunkEndNumber, Path.GetFileName(tmpChunkPath),
							oldSize, newSize);
						_scavengerLog.ChunksNotScavenged(chunkStartNumber, chunkEndNumber, sw.Elapsed,
							"Chunk switch prevented.");
					}
				}
			} catch (FileBeingDeletedException exc) {
				_logger.Information(
					"Got FileBeingDeletedException exception during scavenging, that probably means some chunks were re-replicated."
					+ "\nScavenging of following chunks will be skipped: {oldChunkName}"
					+ "\nStopping scavenging and removing temp chunk '{tmpChunkPath}'..."
					+ "\nException message: {e}.",
					oldChunkName, tmpChunkPath, exc.Message);
				newChunk.Dispose();
				DeleteTempChunk(_logger, tmpChunkPath, MaxRetryCount);
				_scavengerLog.ChunksNotScavenged(chunkStartNumber, chunkEndNumber, sw.Elapsed, exc.Message);
			} catch (OperationCanceledException) {
				_logger.Information("Scavenging cancelled at: {oldChunkName}", oldChunkName);
				newChunk.MarkForDeletion();
				_scavengerLog.ChunksNotScavenged(chunkStartNumber, chunkEndNumber, sw.Elapsed, "Scavenge cancelled");
			} catch (Exception ex) {
				_logger.Information(
					"Got exception while scavenging chunk: #{chunkStartNumber}-{chunkEndNumber}. This chunk will be skipped\n"
					+ "Exception: {e}.", chunkStartNumber, chunkEndNumber, ex.ToString());
				newChunk.Dispose();
				DeleteTempChunk(_logger, tmpChunkPath, MaxRetryCount);
				_scavengerLog.ChunksNotScavenged(chunkStartNumber, chunkEndNumber, sw.Elapsed, ex.Message);
			}
		}

		public static void MergePhase(
			ILogger logger,
			TFChunkDb db,
			long maxChunkDataSize,
			ITFChunkScavengerLog scavengerLog,
			Throttle throttle,
			CancellationToken ct) {

			bool mergedSomething;
			int passNum = 0;
			var sw = new Stopwatch();
			do {
				mergedSomething = false;
				passNum += 1;
				sw.Restart();

				var chunksToMerge = new List<TFChunk.TFChunk>();
				long totalDataSize = 0;
				foreach (var chunk in GetAllChunks(db, 0)) {
					ct.ThrowIfCancellationRequested();

					if (totalDataSize + chunk.PhysicalDataSize > maxChunkDataSize) {
						if (chunksToMerge.Count == 0)
							throw new Exception("SCAVENGING: No chunks to merge, unexpectedly...");

						if (chunksToMerge.Count > 1 &&
							MergeChunks(
								logger: logger,
								db: db,
								scavengerLog: scavengerLog,
								oldChunks: chunksToMerge,
								ct: ct)) {

							mergedSomething = true;
						}

						chunksToMerge.Clear();
						totalDataSize = 0;
					}

					chunksToMerge.Add(chunk);
					totalDataSize += chunk.PhysicalDataSize;
					throttle.Rest(ct);
				}

				if (chunksToMerge.Count > 1) {
					if (MergeChunks(
						logger: logger,
						db: db,
						scavengerLog: scavengerLog,
						oldChunks: chunksToMerge,
						ct: ct)) {

						mergedSomething = true;
					}
				}

				logger.Debug("SCAVENGING: Merge pass #{pass} completed in {elapsed}. {merged} merged.",
					passNum, sw.Elapsed, mergedSomething ? "Some chunks" : "Nothing");
			} while (mergedSomething);
		}

		private static bool MergeChunks(
			ILogger logger,
			TFChunkDb db,
			ITFChunkScavengerLog scavengerLog,
			IList<TFChunk.TFChunk> oldChunks,
			CancellationToken ct) {

			if (oldChunks.IsEmpty()) throw new ArgumentException("Provided list of chunks to merge is empty.");

			var oldChunksList = string.Join("\n", oldChunks);

			if (oldChunks.Count < 2) {
				logger.Debug("SCAVENGING: Tried to merge less than 2 chunks, aborting: {oldChunksList}", oldChunksList);
				return false;
			}

			var sw = Stopwatch.StartNew();

			int chunkStartNumber = oldChunks.First().ChunkHeader.ChunkStartNumber;
			int chunkEndNumber = oldChunks.Last().ChunkHeader.ChunkEndNumber;

			var tmpChunkPath = Path.Combine(db.Config.Path, Guid.NewGuid() + ".merge.scavenge.tmp");
			logger.Debug("SCAVENGING: Started to merge chunks: {oldChunksList}"
			          + "\nResulting temp chunk file: {tmpChunkPath}.",
				oldChunksList, Path.GetFileName(tmpChunkPath));

			TFChunk.TFChunk newChunk;
			try {
				newChunk = TFChunk.TFChunk.CreateNew(
					db.Manager.ChunkCacheManager,
					tmpChunkPath,
					db.Config.ChunkSize,
					chunkStartNumber,
					chunkEndNumber,
					isScavenged: true,
					inMem: db.Config.InMemDb,
					unbuffered: db.Config.Unbuffered,
					writethrough: db.Config.WriteThrough,
					initialReaderCount: db.Config.InitialReaderCount,
					maxReaderCount: db.Config.MaxReaderCount,
					reduceFileCachePressure: db.Config.ReduceFileCachePressure,
					tracker: new TFChunkTracker.NoOp());
			} catch (IOException exc) {
				logger.Error(exc,
					"IOException during creating new chunk for scavenging merge purposes. Stopping scavenging merge process...");
				return false;
			}

			try {
				var oldVersion = oldChunks.Any(x => x.ChunkHeader.Version != TFChunk.TFChunk.CurrentChunkVersion);

				var positionMapping = new List<PosMap>();
				foreach (var oldChunk in oldChunks) {
					var lastFlushedPage = -1;
					TraverseChunkBasic(oldChunk, ct,
						result => {

							positionMapping.Add(WriteRecord(newChunk, result.LogRecord));

							var currentPage = newChunk.RawWriterPosition / 4096;
							if (currentPage - lastFlushedPage > FlushPageInterval) {
								newChunk.Flush();
								lastFlushedPage = currentPage;
							}
						});
				}

				newChunk.CompleteScavenge(positionMapping);

				if (oldVersion) {
					logger.Debug("Forcing merged chunk to be kept as old chunk is a previous version.");
				}

				var chunk = db.Manager.SwitchChunk(newChunk, verifyHash: false, removeChunksWithGreaterNumbers: false);
				if (chunk != null) {
					logger.Debug(
						"Merging of chunks:"
						+ "\n{oldChunksList}"
						+ "\ncompleted in {elapsed}."
						+ "\nNew chunk: {tmpChunkPath} --> #{chunkStartNumber}-{chunkEndNumber} ({newChunk}).",
						oldChunksList, sw.Elapsed, Path.GetFileName(tmpChunkPath), chunkStartNumber, chunkEndNumber,
						Path.GetFileName(chunk.FileName));
					var spaceSaved = oldChunks.Sum(_ => _.FileSize) - newChunk.FileSize;
					scavengerLog.ChunksMerged(chunkStartNumber, chunkEndNumber, sw.Elapsed, spaceSaved);
					return true;
				} else {
					logger.Debug(
						"Merging of chunks:"
						+ "\n{oldChunksList}"
						+ "\ncompleted in {elapsed}."
						+ "\nBut switching was prevented for new chunk: #{chunkStartNumber}-{chunkEndNumber} ({tmpChunkPath}).",
						oldChunksList, sw.Elapsed, chunkStartNumber, chunkEndNumber, Path.GetFileName(tmpChunkPath));
					scavengerLog.ChunksNotMerged(chunkStartNumber, chunkEndNumber, sw.Elapsed,
						"Chunk switch prevented.");
					return false;
				}
			} catch (FileBeingDeletedException exc) {
				logger.Information(
					"Got FileBeingDeletedException exception during scavenge merging, that probably means some chunks were re-replicated."
					+ "\nMerging of following chunks will be skipped:"
					+ "\n{oldChunksList}"
					+ "\nStopping merging and removing temp chunk '{tmpChunkPath}'..."
					+ "\nException message: {e}.",
					oldChunksList, tmpChunkPath, exc.Message);
				newChunk.Dispose();
				DeleteTempChunk(logger, tmpChunkPath, MaxRetryCount);
				scavengerLog.ChunksNotMerged(chunkStartNumber, chunkEndNumber, sw.Elapsed, exc.Message);
				return false;
			} catch (OperationCanceledException) {
				logger.Information("Scavenging cancelled at:"
				         + "\n{oldChunksList}",
					oldChunksList);
				newChunk.MarkForDeletion();
				scavengerLog.ChunksNotMerged(chunkStartNumber, chunkEndNumber, sw.Elapsed, "Scavenge cancelled");
				return false;
			} catch (Exception ex) {
				logger.Information("Got exception while merging chunk:"
				         + "\n{oldChunks}"
				         + "\nException: {e}",
					oldChunks, ex.ToString()
				);
				newChunk.Dispose();
				DeleteTempChunk(logger, tmpChunkPath, MaxRetryCount);
				scavengerLog.ChunksNotMerged(chunkStartNumber, chunkEndNumber, sw.Elapsed, ex.Message);
				return false;
			}
		}

		public static void DeleteTempChunk(ILogger logger, string tmpChunkPath, int retries) {
			try {
				File.SetAttributes(tmpChunkPath, FileAttributes.Normal);
				File.Delete(tmpChunkPath);
			} catch (Exception ex) {
				if (retries > 0) {
					logger.Error("Failed to delete the temp chunk. Retrying {retry}/{maxRetryCount}. Reason: {e}",
						MaxRetryCount - retries, MaxRetryCount, ex);
					Thread.Sleep(5000);
					DeleteTempChunk(logger, tmpChunkPath, retries - 1);
				} else {
					logger.Error("Failed to delete the temp chunk. Retry limit of {maxRetryCount} reached. Reason: {e}",
						MaxRetryCount, ex);
					if (ex is System.IO.IOException)
						ProcessUtil.PrintWhoIsLocking(tmpChunkPath, logger);
					throw;
				}
			}
		}

		private bool ShouldKeep(CandidateRecord result, Dictionary<long, CommitInfo> commits, long chunkStartPos,
			long chunkEndPos) {
			switch (result.LogRecord.RecordType) {
				case LogRecordType.Prepare:
					var prepare = (IPrepareLogRecord<TStreamId>)result.LogRecord;
					if (ShouldKeepPrepare(prepare, commits, chunkStartPos, chunkEndPos))
						return true;
					break;
				case LogRecordType.Commit:
					var commit = (CommitLogRecord)result.LogRecord;
					if (ShouldKeepCommit(commit, commits))
						return true;
					break;
				case LogRecordType.ContentType:
				case LogRecordType.EventType:
				case LogRecordType.Partition:
				case LogRecordType.PartitionType:
				case LogRecordType.Stream:
				case LogRecordType.StreamType:
				case LogRecordType.StreamWrite:
				case LogRecordType.System:
				case LogRecordType.TransactionEnd:
				case LogRecordType.TransactionStart:
					return true;
			}

			return false;
		}

		private bool ShouldKeepCommit(CommitLogRecord commit, Dictionary<long, CommitInfo> commits) {
			CommitInfo commitInfo;
			if (!commits.TryGetValue(commit.TransactionPosition, out commitInfo)) {
				// This should never happen given that we populate `commits` from the commit records.
				// (not sure about this. the `commits` are only commit records for transactions that opened in this chunk)
				return true;
			}

			return commitInfo.KeepCommit != false;
		}

		private bool ShouldKeepPrepare(
			IPrepareLogRecord<TStreamId> prepare,
			Dictionary<long, CommitInfo> commits,
			long chunkStart,
			long chunkEnd) {

			CommitInfo commitInfo;
			bool hasSeenCommit = commits.TryGetValue(prepare.TransactionPosition, out commitInfo);
			bool isCommitted = hasSeenCommit || prepare.Flags.HasAnyOf(PrepareFlags.IsCommitted);

			if (prepare.Flags.HasAnyOf(PrepareFlags.StreamDelete)) {
				// this is the tombstone of a hard deleted stream.
				if (_unsafeIgnoreHardDeletes) {
					_logger.Information(
						"Removing hard deleted stream tombstone for stream {stream} at position {transactionPosition}",
						prepare.EventStreamId, prepare.TransactionPosition);
					commitInfo.TryNotToKeep();
					return false;
				} else {
					commitInfo.ForciblyKeep();
					return true;
				}
			}

			if (!isCommitted && prepare.Flags.HasAnyOf(PrepareFlags.TransactionBegin)) {
				// So here we have prepare which commit is in the following chunks or prepare is not committed at all.
				// Now, whatever heuristic on prepare scavenge we use, we should never delete the very first prepare
				// in transaction, as in some circumstances we need it.
				// For instance, this prepare could be part of ongoing transaction and though we sometimes can determine
				// that prepare wouldn't ever be needed (e.g., stream was deleted, $maxAge or $maxCount rule it out)
				// we still need the first prepare to find out StreamId for possible commit in StorageWriterService.WriteCommit method.
				// There could be other reasons where it is needed, so we just safely filter it out to not bother further.
				return true;
			}

			var lastEventNumber = _readIndex.GetStreamLastEventNumber(prepare.EventStreamId);
			if (lastEventNumber == EventNumber.DeletedStream) {
				// The stream is hard deleted but this is not the tombstone.
				// When all prepares and commit of transaction belong to single chunk and the stream is deleted,
				// we can safely delete both prepares and commit.
				// Even if this prepare is not committed, but its stream is deleted, then as long as it is
				// not TransactionBegin prepare we can remove it, because any transaction should fail either way on commit stage.
				// (see comments in previous section for TransactionBegin)
				commitInfo.TryNotToKeep();
				return false;
			}

			if (!isCommitted) {
				// If we could somehow figure out (from read index) the event number of this prepare
				// (if it is actually committed, but commit is in another chunk) then we can apply same scavenging logic.
				// Unfortunately, if it is not committed prepare we can say nothing for now, so should conservatively keep it.
				return true;
			}

			if (prepare.Flags.HasNoneOf(PrepareFlags.Data)) {
				// We encountered system prepare with no data. As of now it can appear only in explicit
				// transactions so we can safely remove it. The performance shouldn't hurt, because
				// TransactionBegin prepare is never needed either way and TransactionEnd should be in most
				// circumstances close to commit, so shouldn't hurt performance too much.
				// The advantage of getting rid of system prepares is ability to completely eliminate transaction
				// prepares and commit, if transaction events are completely ruled out by $maxAge/$maxCount.
				// Otherwise we'd have to either keep prepare not requiring to keep commit, which could leave
				// this prepare as never discoverable garbage, or we could insist on keeping commit forever
				// even if all events in transaction are scavenged.
				commitInfo.TryNotToKeep();
				return false;
			}

			if (IsSoftDeletedTempStreamWithinSameChunk(prepare.EventStreamId, chunkStart, chunkEnd)) {
				commitInfo.TryNotToKeep();
				return false;
			}

			var eventNumber = prepare.Flags.HasAnyOf(PrepareFlags.IsCommitted)
				? prepare.ExpectedVersion + 1 // IsCommitted prepares always have explicit expected version
				// we always have commitInfo.EventNumber here because we early returned if isCommitted is false
				: commitInfo.EventNumber + prepare.TransactionOffset;

			if (DiscardBecauseDuplicate(prepare, eventNumber)) {
				commitInfo.TryNotToKeep();
				return false;
			}

			// We should always physically keep the very last prepare in the stream.
			// Otherwise we get into trouble when trying to resolve LastStreamEventNumber, for instance.
			// That is because our TableIndex doesn't keep EventStreamId, only hash of it, so on doing some operations
			// that needs TableIndex, we have to make sure we have prepare records in TFChunks when we need them.
			if (eventNumber >= lastEventNumber) {
				// Definitely keep commit, otherwise current prepare wouldn't be discoverable.
				commitInfo.ForciblyKeep();
				return true;
			}

			var meta = _readIndex.GetStreamMetadata(prepare.EventStreamId);
			bool canRemove = (meta.MaxCount.HasValue && eventNumber < lastEventNumber - meta.MaxCount.Value + 1)
			                 || (meta.TruncateBefore.HasValue && eventNumber < meta.TruncateBefore.Value)
			                 || (meta.MaxAge.HasValue && prepare.TimeStamp < DateTime.UtcNow - meta.MaxAge.Value);

			if (canRemove) {
				commitInfo.TryNotToKeep();
				return false;
			} else {
				commitInfo.ForciblyKeep();
				return true;
			}
		}

		private bool DiscardBecauseDuplicate(IPrepareLogRecord<TStreamId> prepare, long eventNumber) {
			var result = _readIndex.ReadEvent(IndexReader.UnspecifiedStreamName, prepare.EventStreamId, eventNumber);
			if (result.Result == ReadEventResult.Success && result.Record.LogPosition != prepare.LogPosition) {
				// prepare isn't the record we get for an index read at its own stream/version.
				// therefore it is a duplicate that cannot be read from the index, discard it.
				return true;
			}

			return false;
		}

		private bool IsSoftDeletedTempStreamWithinSameChunk(TStreamId eventStreamId, long chunkStart, long chunkEnd) {
			TStreamId sh;
			TStreamId msh;
			if (_metastreams.IsMetaStream(eventStreamId)) {
				var originalStreamId = _metastreams.OriginalStreamOf(eventStreamId);
				var meta = _readIndex.GetStreamMetadata(originalStreamId);
				if (meta.TruncateBefore != EventNumber.DeletedStream || meta.TempStream != true)
					return false;
				sh = originalStreamId;
				msh = eventStreamId;
			} else {
				var meta = _readIndex.GetStreamMetadata(eventStreamId);
				if (meta.TruncateBefore != EventNumber.DeletedStream || meta.TempStream != true)
					return false;
				sh = eventStreamId;
				msh = _metastreams.MetaStreamOf(eventStreamId);
			}

			IndexEntry e;
			var allInChunk = _tableIndex.TryGetOldestEntry(sh, out e) && e.Position >= chunkStart &&
			                 e.Position < chunkEnd
			                 && _tableIndex.TryGetLatestEntry(sh, out e) && e.Position >= chunkStart &&
			                 e.Position < chunkEnd
			                 && _tableIndex.TryGetOldestEntry(msh, out e) && e.Position >= chunkStart &&
			                 e.Position < chunkEnd
			                 && _tableIndex.TryGetLatestEntry(msh, out e) && e.Position >= chunkStart &&
			                 e.Position < chunkEnd;
			return allInChunk;
		}

		private static void TraverseChunkBasic(TFChunk.TFChunk chunk, CancellationToken ct,
			Action<CandidateRecord> process) {
			var result = chunk.TryReadFirst();
			while (result.Success) {
				process(new CandidateRecord(result.LogRecord, result.RecordLength));

				ct.ThrowIfCancellationRequested();

				result = chunk.TryReadClosestForward(result.NextPosition);
			}
		}

		public static PosMap WriteRecord(TFChunk.TFChunk newChunk, ILogRecord record) {
			var writeResult = newChunk.TryAppend(record);
			if (!writeResult.Success) {
				throw new Exception(string.Format(
					"Unable to append record during scavenging. Scavenge position: {0}, Record: {1}.",
					writeResult.OldPosition,
					record));
			}

			long logPos = newChunk.ChunkHeader.GetLocalLogPosition(record.LogPosition);
			int actualPos = (int)writeResult.OldPosition;
			return new PosMap(logPos, actualPos);
		}

		internal class CommitInfo {
			public readonly long EventNumber;

			public bool? KeepCommit;

			public CommitInfo(CommitLogRecord commitRecord) {
				EventNumber = commitRecord.FirstEventNumber;
			}

			public override string ToString() {
				return string.Format("EventNumber: {0}, KeepCommit: {1}", EventNumber, KeepCommit);
			}
		}

		struct CandidateRecord {
			public readonly ILogRecord LogRecord;
			public readonly int RecordLength;

			public CandidateRecord(ILogRecord logRecord, int recordLength) {
				LogRecord = logRecord;
				RecordLength = recordLength;
			}
		}

		private ObjectPool<ThreadLocalScavengeCache> CreateThreadLocalScavengeCachePool(int threads) {
			const int initialSizeOfThreadLocalCache = 1024 * 64; // 64k records

			return new ObjectPool<ThreadLocalScavengeCache>(
				ScavengeId,
				0,
				threads,
				() => {
					_logger.Debug("SCAVENGING: Allocating {size} spaces in thread local cache {threadId}.",
						initialSizeOfThreadLocalCache,
						Thread.CurrentThread.ManagedThreadId);
					return new ThreadLocalScavengeCache(initialSizeOfThreadLocalCache);
				},
				cache => { },
				pool => { });
		}

		class ThreadLocalScavengeCache {
			private readonly Dictionary<long, CommitInfo> _commits;
			private readonly List<CandidateRecord> _records;

			public Dictionary<long, CommitInfo> Commits {
				get { return _commits; }
			}

			public List<CandidateRecord> Records {
				get { return _records; }
			}

			public ThreadLocalScavengeCache(int records) {
				// assume max quarter records are commits.
				_commits = new Dictionary<long, CommitInfo>(records / 4);
				_records = new List<CandidateRecord>(records);
			}

			public void Reset() {
				Commits.Clear();
				Records.Clear();
			}
		}
	}

	internal static class CommitInfoExtensions {
		public static void ForciblyKeep<TStreamId>(this TFChunkScavenger<TStreamId>.CommitInfo commitInfo) {
			if (commitInfo != null)
				commitInfo.KeepCommit = true;
		}

		public static void TryNotToKeep<TStreamId>(this TFChunkScavenger<TStreamId>.CommitInfo commitInfo) {
			// If someone decided definitely to keep corresponding commit then we shouldn't interfere.
			// Otherwise we should point that yes, you can remove commit for this prepare.
			if (commitInfo != null)
				commitInfo.KeepCommit = commitInfo.KeepCommit ?? false;
		}
	}
}
