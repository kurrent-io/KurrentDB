using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Exceptions;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.TransactionLog.Chunks {
	public class TFChunkDb : IDisposable {
		public readonly TFChunkDbConfig Config;
		public readonly TFChunkManager Manager;

		private readonly ILogger _log;
		private readonly ITransactionFileTracker _tracker;
		private int _closed;

		public TFChunkDb(TFChunkDbConfig config, IChunkCacheManager cacheManager = null, ITransactionFileTracker tracker = null, ILogger log = null) {
			Ensure.NotNull(config, "config");

			Config = config;
			_tracker = tracker ?? new TFChunkTracker.NoOp();
			cacheManager ??= new UnmanagedChunkCacheManager();
			Manager = new TFChunkManager(Config, cacheManager, _tracker);
			_log = log ?? Serilog.Log.ForContext<TFChunkDb>();
		}

		struct ChunkInfo {
			public int ChunkStartNumber;
			public string ChunkFileName;
		}

		IEnumerable<ChunkInfo> GetAllLatestChunksExceptLast(TFChunkEnumerator chunkEnumerator, int lastChunkNum) {
			foreach (var chunkInfo in chunkEnumerator.EnumerateChunks(lastChunkNum)) {
				switch (chunkInfo) {
					case LatestVersion(var fileName, var start, _):
						if (start <= lastChunkNum - 1)
							yield return new ChunkInfo { ChunkFileName = fileName, ChunkStartNumber = start };
						break;
					case MissingVersion(var fileName, var start):
						if (start <= lastChunkNum - 1)
							throw new CorruptDatabaseException(new ChunkNotFoundException(fileName));
						break;
				}
			}
		}

		public void Open(bool verifyHash = true, bool readOnly = false, int threads = 1) {
			Ensure.Positive(threads, "threads");

			ValidateReaderChecksumsMustBeLess(Config);
			var checkpoint = Config.WriterCheckpoint.Read();

			if (Config.InMemDb) {
				Manager.AddNewChunk();
				return;
			}

			var lastChunkNum = (int)(checkpoint / Config.ChunkSize);
			var lastChunkVersions = Config.FileNamingStrategy.GetAllVersionsFor(lastChunkNum);
			var chunkEnumerator = new TFChunkEnumerator(Config.FileNamingStrategy);

			try {
				Parallel.ForEach(GetAllLatestChunksExceptLast(chunkEnumerator, lastChunkNum), // the last chunk is dealt with separately
					new ParallelOptions {MaxDegreeOfParallelism = threads},
					chunkInfo => {
						TFChunk.TFChunk chunk;
						if (lastChunkVersions.Length == 0 &&
						    (chunkInfo.ChunkStartNumber + 1) * (long)Config.ChunkSize == checkpoint) {
							// The situation where the logical data size is exactly divisible by ChunkSize,
							// so it might happen that we have checkpoint indicating one more chunk should exist,
							// but the actual last chunk is (lastChunkNum-1) one and it could be not completed yet -- perfectly valid situation.
							var footer = ReadChunkFooter(chunkInfo.ChunkFileName);
							if (footer.IsCompleted)
								chunk = TFChunk.TFChunk.FromCompletedFile(
									Manager.ChunkCacheManager,
									chunkInfo.ChunkFileName, verifyHash: false,
									unbufferedRead: Config.Unbuffered,
									initialReaderCount: Config.InitialReaderCount,
									maxReaderCount: Config.MaxReaderCount,
									tracker: _tracker,
									optimizeReadSideCache: Config.OptimizeReadSideCache,
									reduceFileCachePressure: Config.ReduceFileCachePressure);
							else {
								chunk = TFChunk.TFChunk.FromOngoingFile(
									Manager.ChunkCacheManager,
									chunkInfo.ChunkFileName, Config.ChunkSize,
									checkSize: false,
									unbuffered: Config.Unbuffered,
									writethrough: Config.WriteThrough, initialReaderCount: Config.InitialReaderCount,
									maxReaderCount: Config.MaxReaderCount,
									reduceFileCachePressure: Config.ReduceFileCachePressure,
									tracker: _tracker);
								// chunk is full with data, we should complete it right here
								if (!readOnly)
									chunk.Complete();
							}
						} else {
							chunk = TFChunk.TFChunk.FromCompletedFile(
								Manager.ChunkCacheManager,
								chunkInfo.ChunkFileName, verifyHash: false,
								unbufferedRead: Config.Unbuffered,
								initialReaderCount: Config.InitialReaderCount,
								maxReaderCount: Config.MaxReaderCount,
								optimizeReadSideCache: Config.OptimizeReadSideCache,
								reduceFileCachePressure: Config.ReduceFileCachePressure,
								tracker: _tracker);
						}

						// This call is theadsafe.
						Manager.AddChunk(chunk);
					});
			} catch (AggregateException aggEx) {
				// We only really care that *something* is wrong - throw the first inner exception.
				throw aggEx.InnerException;
			}

			if (lastChunkVersions.Length == 0) {
				var onBoundary = checkpoint == (Config.ChunkSize * (long)lastChunkNum);
				if (!onBoundary)
					throw new CorruptDatabaseException(
						new ChunkNotFoundException(Config.FileNamingStrategy.GetFilenameFor(lastChunkNum, 0)));
				if (!readOnly)
					Manager.AddNewChunk();
			} else {
				var chunkFileName = lastChunkVersions[0];
				var chunkHeader = ReadChunkHeader(chunkFileName);
				var chunkLocalPos = chunkHeader.GetLocalLogPosition(checkpoint);
				if (chunkHeader.IsScavenged) {
					var lastChunk = TFChunk.TFChunk.FromCompletedFile(
						Manager.ChunkCacheManager,
						chunkFileName, verifyHash: false,
						unbufferedRead: Config.Unbuffered,
						initialReaderCount: Config.InitialReaderCount,
						maxReaderCount: Config.MaxReaderCount,
						optimizeReadSideCache: Config.OptimizeReadSideCache,
						reduceFileCachePressure: Config.ReduceFileCachePressure,
						tracker: _tracker);
					if (lastChunk.ChunkFooter.LogicalDataSize != chunkLocalPos) {
						lastChunk.Dispose();
						throw new CorruptDatabaseException(new BadChunkInDatabaseException(
							string.Format("Chunk {0} is corrupted. Expected local chunk position: {1}, "
							              + "but Chunk.LogicalDataSize is {2} (Chunk.PhysicalDataSize is {3}). Writer checkpoint: {4}.",
								chunkFileName, chunkLocalPos, lastChunk.LogicalDataSize, lastChunk.PhysicalDataSize,
								checkpoint)));
					}

					Manager.AddChunk(lastChunk);
					if (!readOnly) {
						_log.Information(
							"Moving WriterCheckpoint from {checkpoint} to {chunkEndPosition}, as it points to the scavenged chunk. "
							+ "If that was not caused by replication of scavenged chunks, that could be a bug.",
							checkpoint, lastChunk.ChunkHeader.ChunkEndPosition);
						Config.WriterCheckpoint.Write(lastChunk.ChunkHeader.ChunkEndPosition);
						Config.WriterCheckpoint.Flush();
						Manager.AddNewChunk();
					}
				} else {
					var lastChunk = TFChunk.TFChunk.FromOngoingFile(
						Manager.ChunkCacheManager,
						chunkFileName, (int)chunkLocalPos, checkSize: false,
						unbuffered: Config.Unbuffered,
						writethrough: Config.WriteThrough, initialReaderCount: Config.InitialReaderCount,
						maxReaderCount: Config.MaxReaderCount,
						reduceFileCachePressure: Config.ReduceFileCachePressure,
						tracker: _tracker);
					Manager.AddChunk(lastChunk);
				}
			}

			_log.Information("Ensuring no excessive chunks...");
			EnsureNoExcessiveChunks(chunkEnumerator, lastChunkNum);
			_log.Information("Done ensuring no excessive chunks.");

			if (!readOnly) {
				_log.Information("Removing old chunk versions...");
				RemoveOldChunksVersions(chunkEnumerator, lastChunkNum);
				_log.Information("Done removing old chunk versions.");

				_log.Information("Cleaning up temp files...");
				CleanUpTempFiles();
				_log.Information("Done cleaning up temp files.");
			}

			if (verifyHash && lastChunkNum > 0) {
				var preLastChunk = Manager.GetChunk(lastChunkNum - 1);
				var lastBgChunkNum = preLastChunk.ChunkHeader.ChunkStartNumber;
				ThreadPool.QueueUserWorkItem(_ => {
					for (int chunkNum = lastBgChunkNum; chunkNum >= 0;) {
						var chunk = Manager.GetChunk(chunkNum);
						try {
							chunk.VerifyFileHash();
						} catch (FileBeingDeletedException exc) {
							_log.Debug(
								"{exceptionType} exception was thrown while doing background validation of chunk {chunk}.",
								exc.GetType().Name, chunk);
							_log.Debug(
								"That's probably OK, especially if truncation was request at the same time: {e}.",
								exc.Message);
						} catch (Exception exc) {
							_log.Fatal(exc, "Verification of chunk {chunk} failed, terminating server...",
								chunk);
							var msg = string.Format("Verification of chunk {0} failed, terminating server...", chunk);
							Application.Exit(ExitCode.Error, msg);
							return;
						}

						chunkNum = chunk.ChunkHeader.ChunkStartNumber - 1;
					}
				});
			}

			Manager.EnableCaching();
		}

		private void ValidateReaderChecksumsMustBeLess(TFChunkDbConfig config) {
			var current = config.WriterCheckpoint.Read();
			foreach (var checkpoint in new[] {config.ChaserCheckpoint, config.EpochCheckpoint}) {
				if (checkpoint.Read() > current)
					throw new CorruptDatabaseException(new ReaderCheckpointHigherThanWriterException(checkpoint.Name));
			}
		}

		private static ChunkHeader ReadChunkHeader(string chunkFileName) {
			ChunkHeader chunkHeader;
			using (var fs = new FileStream(chunkFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
				if (fs.Length < ChunkFooter.Size + ChunkHeader.Size) {
					throw new CorruptDatabaseException(new BadChunkInDatabaseException(
						string.Format(
							"Chunk file '{0}' is bad. It does not have enough size for header and footer. File size is {1} bytes.",
							chunkFileName, fs.Length)));
				}

				chunkHeader = ChunkHeader.FromStream(fs);
			}

			return chunkHeader;
		}

		private static ChunkFooter ReadChunkFooter(string chunkFileName) {
			ChunkFooter chunkFooter;
			using (var fs = new FileStream(chunkFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
				if (fs.Length < ChunkFooter.Size + ChunkHeader.Size) {
					throw new CorruptDatabaseException(new BadChunkInDatabaseException(
						string.Format(
							"Chunk file '{0}' is bad. It does not have enough size for header and footer. File size is {1} bytes.",
							chunkFileName, fs.Length)));
				}

				fs.Seek(-ChunkFooter.Size, SeekOrigin.End);
				chunkFooter = ChunkFooter.FromStream(fs);
			}

			return chunkFooter;
		}

		private static void EnsureNoExcessiveChunks(TFChunkEnumerator chunkEnumerator, int lastChunkNum) {
			var extraneousFiles = new List<string>();

			foreach (var chunkInfo in chunkEnumerator.EnumerateChunks(lastChunkNum)) {
				switch (chunkInfo) {
					case LatestVersion(var fileName, var start, _):
						if (start > lastChunkNum)
							extraneousFiles.Add(fileName);
						break;
					case OldVersion(var fileName, var start):
						if (start > lastChunkNum)
							extraneousFiles.Add(fileName);
						break;
				}
			}

			if (!extraneousFiles.IsEmpty()) {
				throw new CorruptDatabaseException(new ExtraneousFileFoundException(
					$"Unexpected files: {string.Join(", ", extraneousFiles)}."));
			}
		}

		private void RemoveOldChunksVersions(TFChunkEnumerator chunkEnumerator, int lastChunkNum) {
			foreach (var chunkInfo in chunkEnumerator.EnumerateChunks(lastChunkNum)) {
				switch (chunkInfo) {
					case OldVersion(var fileName, var start):
						if (start <= lastChunkNum)
							RemoveFile("Removing old chunk version: {chunk}...", fileName);
						break;
				}
			}
		}

		private void CleanUpTempFiles() {
			var tempFiles = Config.FileNamingStrategy.GetAllTempFiles();
			foreach (string tempFile in tempFiles) {
				try {
					RemoveFile("Deleting temporary file {file}...", tempFile);
				} catch (Exception exc) {
					_log.Error(exc, "Error while trying to delete remaining temp file: '{tempFile}'.",
						tempFile);
				}
			}
		}

		private void RemoveFile(string reason, string file) {
			_log.Debug(reason, file);
			File.SetAttributes(file, FileAttributes.Normal);
			File.Delete(file);
		}

		public void Dispose() {
			Close();
		}

		public void Close() {
			if (Interlocked.CompareExchange(ref _closed, 1, 0) != 0)
				return;

			bool chunksClosed = false;

			try {
				chunksClosed = Manager.TryClose();
			} catch (Exception ex) {
				_log.Error(ex, "An error has occurred while closing the chunks.");
			}

			if (!chunksClosed)
				_log.Debug("One or more chunks are still open; skipping checkpoint flush.");

			Config.WriterCheckpoint.Close(flush: chunksClosed);
			Config.ChaserCheckpoint.Close(flush: chunksClosed);
			Config.EpochCheckpoint.Close(flush: chunksClosed);
			Config.TruncateCheckpoint.Close(flush: chunksClosed);
			Config.ProposalCheckpoint.Close(flush: chunksClosed);
			Config.StreamExistenceFilterCheckpoint.Close(flush: chunksClosed);
		}
	}
}
