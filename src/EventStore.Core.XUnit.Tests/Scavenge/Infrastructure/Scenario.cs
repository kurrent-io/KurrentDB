using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Caching;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using EventStore.Core.Index;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Settings;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Index.Hashers;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.Util;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.StreamMetadatas;

#pragma warning disable CS0162 // Unreachable code detected

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class Scenario {
		protected const int Threads = 1;
		public const bool CollideEverything = false;
	}

	// sort of similar to ScavengeTestScenario
	public class Scenario<TLogFormat, TStreamId> : Scenario {
		private static EqualityComparer<TStreamId> StreamIdComparer { get; } =
			EqualityComparer<TStreamId>.Default;

		private Func<TFChunkDbConfig, LogFormatAbstractor<TStreamId>, DbResult> _getDb;
		private Func<ScavengeStateBuilder<TStreamId>, ScavengeStateBuilder<TStreamId>> _stateTransform;
		private Action<ScavengeState<TStreamId>> _assertState;
		private List<ScavengePoint> _newScavengePoint;
		private ITFChunkScavengerLog _logger;

		private bool _mergeChunks;
		private bool _syncOnly;
		private string _dbPath;
		private TStreamId _accumulatingCancellationTrigger;
		private TStreamId _calculatingCancellationTrigger;
		private TStreamId _executingChunkCancellationTrigger;
		private TStreamId _executingIndexEntryCancellationTrigger;
		private Type _cancelWhenCheckpointingType;
		private (string Message, int Line)[] _expectedTrace;
		private bool _unsafeIgnoreHardDeletes;
		private readonly HashSet<int> _chunkNumsToEmpty = new();

		protected Tracer Tracer { get; set; }

		public Scenario() {
			_getDb = (_, _) => throw new Exception("db not configured. call WithDb");
			_stateTransform = x => x;
			Tracer = new Tracer();
		}

		public Scenario<TLogFormat, TStreamId> WithTracerFrom(Scenario<TLogFormat, TStreamId> scenario) {
			Tracer = scenario.Tracer;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> WithUnsafeIgnoreHardDeletes(
			bool unsafeIgnoreHardDeletes = true) {

			_unsafeIgnoreHardDeletes = unsafeIgnoreHardDeletes;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> WithDbPath(string path) {
			_dbPath = path;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> WithDb(DbResult db) {
			_getDb = (_, _) => {
				db.Db.Open();
				return db;
			};
			return this;
		}

		public Scenario<TLogFormat, TStreamId> WithDb(
			Func<
				TFChunkDbCreationHelper<TLogFormat, TStreamId>,
				TFChunkDbCreationHelper<TLogFormat, TStreamId>> f) {

			_getDb = (dbConfig, logFormat) =>
				f(new TFChunkDbCreationHelper<TLogFormat, TStreamId>(dbConfig, logFormat)).CreateDb();
			return this;
		}

		public Scenario<TLogFormat, TStreamId> WithLogger(ITFChunkScavengerLog logger) {
			_logger = logger;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> WithState(
			Func<ScavengeStateBuilder<TStreamId>, ScavengeStateBuilder<TStreamId>> f) {

			var wrapped = _stateTransform;
			_stateTransform = builder => builder
				.TransformBuilder(wrapped)
				.TransformBuilder(f);
			return this;
		}

		public Scenario<TLogFormat, TStreamId> AssertState(Action<ScavengeState<TStreamId>> f) {
			_assertState = f;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> MutateState(Action<ScavengeState<TStreamId>> f) {
			var wrapped = _stateTransform;
			_stateTransform = builder => builder
				.TransformBuilder(wrapped)
				.MutateState(f);
			return this;
		}

		public Scenario<TLogFormat, TStreamId> WithMergeChunks(bool mergeChunks = true) {
			_mergeChunks = mergeChunks;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> WithSyncOnly(bool syncOnly = true) {
			_syncOnly = syncOnly;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> CancelOnNewScavengePoint(
			List<ScavengePoint> newScavengePoint) {

			_newScavengePoint = newScavengePoint;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> CancelWhenAccumulatingMetaRecordFor(TStreamId trigger) {
			_accumulatingCancellationTrigger = trigger;
			return this;
		}

		// note for this to work the trigger stream needs metadata so it will be calculated
		// and it needs to have at least one record
		public Scenario<TLogFormat, TStreamId> CancelWhenCalculatingOriginalStream(TStreamId trigger) {
			_calculatingCancellationTrigger = trigger;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> CancelWhenExecutingChunk(TStreamId trigger) {
			_executingChunkCancellationTrigger = trigger;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> CancelWhenExecutingIndexEntry(TStreamId trigger) {
			_executingIndexEntryCancellationTrigger = trigger;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> CancelWhenCheckpointing<TCheckpoint>() {
			_cancelWhenCheckpointingType = typeof(TCheckpoint);
			return this;
		}

		// Assert methods can be used to input checks that are internal to the scavenge
		// This is not black box testing, handle with care.
		public delegate Scenario<TLogFormat, TStreamId> TraceDelegate(params string[] expected);

		public Scenario<TLogFormat, TStreamId> AssertTrace(params (string, int)[] expected) {
			_expectedTrace = expected;
			return this;
		}

		public Scenario<TLogFormat, TStreamId> EmptyChunk(int chunkNumber) {
			_chunkNumsToEmpty.Add(chunkNumber);
			return this;
		}

		public async Task<DbResult> RunAsync(
			Func<DbResult, ILogRecord[][]> getExpectedKeptRecords = null,
			Func<DbResult, ILogRecord[][]> getExpectedKeptIndexEntries = null) {

			return await RunInternalAsync(
				getExpectedKeptRecords,
				getExpectedKeptIndexEntries);
		}

		private async Task<DbResult> RunInternalAsync(
			Func<DbResult, ILogRecord[][]> getExpectedKeptRecords,
			Func<DbResult, ILogRecord[][]> getExpectedKeptIndexEntries) {

			if (string.IsNullOrEmpty(_dbPath))
				throw new Exception("call WithDbPath");

			var indexPath = Path.Combine(_dbPath, "index");
			var logFormat = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory.Create(new() {
				IndexDirectory = indexPath,
			});

			var dbConfig = TFChunkHelper.CreateSizedDbConfig(_dbPath, 0, chunkSize: 1024 * 1024);
			var dbResult = _getDb(dbConfig, logFormat);
			var keptRecords = getExpectedKeptRecords != null
				? getExpectedKeptRecords(dbResult)
				: null;

			var keptIndexEntries = getExpectedKeptIndexEntries != null
				? getExpectedKeptIndexEntries(dbResult)
				: keptRecords;

			dbResult.Db.Config.WriterCheckpoint.Flush();
			dbResult.Db.Config.ChaserCheckpoint.Write(dbResult.Db.Config.WriterCheckpoint.Read());
			dbResult.Db.Config.ChaserCheckpoint.Flush();
			dbResult.Db.Config.ReplicationCheckpoint.Write(dbResult.Db.Config.WriterCheckpoint.Read());
			dbResult.Db.Config.ReplicationCheckpoint.Flush();

			var readerPool = new ObjectPool<ITransactionFileReader>(
				objectPoolName: "ReadIndex readers pool",
				initialCount: ESConsts.PTableInitialReaderCount,
				maxCount: ESConsts.PTableInitialReaderCount,
				factory: () => new TFChunkReader(dbResult.Db, dbResult.Db.Config.WriterCheckpoint));

			var lowHasher = logFormat.LowHasher;
			var highHasher = logFormat.HighHasher;

			var humanHashers = true;
			if (CollideEverything) {
				if (LogFormatHelper<TLogFormat, TStreamId>.IsV3)
					throw new Exception("Cant cause collisions in V3");
				lowHasher = new ConstantHasher(0) as IHasher<TStreamId>;
				highHasher = new ConstantHasher(0) as IHasher<TStreamId>;
			} else if (humanHashers) {
				if (LogFormatHelper<TLogFormat, TStreamId>.IsV3)
					throw new Exception("Cant cause collisions in V3");
				lowHasher = new ConstantHasher(0) as IHasher<TStreamId>;
				highHasher = new HumanReadableHasher32() as IHasher<TStreamId>;
			}

			var hasher = new CompositeHasher<TStreamId>(lowHasher, highHasher);

			var tableIndex = new TableIndex<TStreamId>(
				directory: indexPath,
				lowHasher: lowHasher,
				highHasher: highHasher,
				emptyStreamId: logFormat.EmptyStreamId,
				memTableFactory: () => new HashListMemTable(PTableVersions.IndexV4, maxSize: 200),
				tfReaderFactory: () => new TFReaderLease(readerPool),
				ptableVersion: PTableVersions.IndexV4,
				maxAutoMergeIndexLevel: int.MaxValue,
				pTableMaxReaderCount: ESConsts.PTableInitialReaderCount,
				maxSizeForMemory: 1, // convert everything to ptables immediately
				maxTablesPerLevel: 2,
				inMem: false);
			logFormat.StreamNamesProvider.SetTableIndex(tableIndex);

			var readIndex = new ReadIndex<TStreamId>(
				bus: new NoopPublisher(),
				readerPool: readerPool,
				tableIndex: tableIndex,
				logFormat.StreamNameIndexConfirmer,
				logFormat.StreamIds,
				logFormat.StreamNamesProvider,
				logFormat.EmptyStreamId,
				logFormat.StreamIdValidator,
				logFormat.StreamIdSizer,
				logFormat.StreamExistenceFilter,
				logFormat.StreamExistenceFilterReader,
				logFormat.EventTypeIndexConfirmer,
				new LRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached>("LastEventNumber", 100),
				new LRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached>("StreamMetadata", 100),
				additionalCommitChecks: true,
				metastreamMaxCount: 1,
				hashCollisionReadLimit: Opts.HashCollisionReadLimitDefault,
				skipIndexScanOnReads: Opts.SkipIndexScanOnReadsDefault,
				replicationCheckpoint: dbResult.Db.Config.ReplicationCheckpoint,
				indexCheckpoint: dbResult.Db.Config.IndexCheckpoint,
				indexStatusTracker: new IndexStatusTracker.NoOp(),
				indexTracker: new IndexTracker.NoOp(),
				cacheTracker: new CacheHitsMissesTracker.NoOp());

			readIndex.IndexCommitter.Init(dbResult.Db.Config.WriterCheckpoint.Read());
			// wait for tables to be merged. for one of the tests this takes a while
			for (int i = 0; i < 10; i++) {
				try {
					tableIndex.WaitForBackgroundTasks();
					break;
				} catch {
				}
			}

			EmptyRequestedChunks(dbResult.Db);

			Scavenger<TStreamId> sut = null;
			try {
				var cancellationTokenSource = new CancellationTokenSource();
				var metastreamLookup = logFormat.Metastreams;

				var scavengeState = new ScavengeStateBuilder<TStreamId>(hasher, metastreamLookup)
					.TransformBuilder(_stateTransform)
					.CancelWhenCheckpointing(_cancelWhenCheckpointingType, cancellationTokenSource)
					.WithTracer(Tracer)
					.Build();

				IChunkReaderForAccumulator<TStreamId> chunkReader = new ChunkReaderForAccumulator<TStreamId>(
					dbResult.Db.Manager,
					metastreamLookup,
					logFormat.StreamIdConverter,
					dbResult.Db.Config.ReplicationCheckpoint,
					dbConfig.ChunkSize);

				var indexReader = new IndexReaderForAccumulator<TStreamId>(readIndex);

				var accumulatorMetastreamLookup = new AdHocMetastreamLookupInterceptor<TStreamId>(
					metastreamLookup,
					(continuation, streamId) => {
						if (StreamIdComparer.Equals(streamId, _accumulatingCancellationTrigger))
							cancellationTokenSource.Cancel();
						return continuation(streamId);
					});

				var calculatorIndexReader = new AdHocIndexReaderInterceptor<TStreamId>(
					new IndexReaderForCalculator<TStreamId>(
						readIndex,
						() => new TFReaderLease(readerPool),
						scavengeState.LookupUniqueHashUser),
					(f, handle, from, maxCount, x) => {
						if (_calculatingCancellationTrigger != null)
							if ((handle.Kind == StreamHandle.Kind.Hash && handle.StreamHash == hasher.Hash(_calculatingCancellationTrigger)) ||
								(handle.Kind == StreamHandle.Kind.Id && StreamIdComparer.Equals(handle.StreamId, _calculatingCancellationTrigger))) {

								cancellationTokenSource.Cancel();
							}
						return f(handle, from, maxCount, x);
					});

				var chunkExecutorMetastreamLookup = new AdHocMetastreamLookupInterceptor<TStreamId>(
					metastreamLookup,
					(continuation, streamId) => {
						if (StreamIdComparer.Equals(streamId, _executingChunkCancellationTrigger))
							cancellationTokenSource.Cancel();
						return continuation(streamId);
					});

				var indexScavenger = new IndexScavenger(tableIndex);
				var cancellationWrappedIndexScavenger = new AdHocIndexScavengerInterceptor(
					indexScavenger,
					f => entry => {
						if (_executingIndexEntryCancellationTrigger != null &&
							entry.Stream == hasher.Hash(_executingIndexEntryCancellationTrigger)) {

							cancellationTokenSource.Cancel();
						}
						return f(entry);
					});

				var cancellationCheckPeriod = 1;
				var checkpointPeriod = 2;
				var restPeriod = 5;

				// add tracing
				chunkReader = new TracingChunkReaderForAccumulator<TStreamId>(chunkReader, Tracer.Trace);

				var logger = Serilog.Log.Logger;

				var throttle = new Throttle(
					logger,
					TimeSpan.FromMilliseconds(1000),
					TimeSpan.FromMilliseconds(1000),
					activePercent: 100);

				IAccumulator<TStreamId> accumulator = new Accumulator<TStreamId>(
					logger: logger,
					chunkSize: dbConfig.ChunkSize,
					metastreamLookup: accumulatorMetastreamLookup,
					chunkReader: chunkReader,
					index: indexReader,
					cancellationCheckPeriod: cancellationCheckPeriod,
					throttle: throttle);

				ICalculator<TStreamId> calculator = new Calculator<TStreamId>(
					logger: logger,
					index: calculatorIndexReader,
					chunkSize: dbConfig.ChunkSize,
					cancellationCheckPeriod: cancellationCheckPeriod,
					buffer: new(checkpointPeriod),
					throttle: throttle);

				IChunkExecutor<TStreamId> chunkExecutor = new ChunkExecutor<TStreamId, ILogRecord>(
					logger: logger,
					metastreamLookup: chunkExecutorMetastreamLookup,
					chunkManager: new TracingChunkManagerForChunkExecutor<TStreamId, ILogRecord>(
						new ChunkManagerForExecutor<TStreamId>(
							logger,
							dbResult.Db.Manager,
							dbConfig),
						Tracer),
					chunkSize: dbConfig.ChunkSize,
					unsafeIgnoreHardDeletes: _unsafeIgnoreHardDeletes,
					cancellationCheckPeriod: cancellationCheckPeriod,
					threads: Threads,
					throttle: throttle);

				IChunkMerger chunkMerger = new ChunkMerger(
					logger: logger,
					mergeChunks: _mergeChunks,
					new OldScavengeChunkMergerBackend(logger, dbResult.Db),
					throttle: throttle);

				IIndexExecutor<TStreamId> indexExecutor = new IndexExecutor<TStreamId>(
					logger: logger,
					indexScavenger: cancellationWrappedIndexScavenger,
					streamLookup: new ChunkReaderForIndexExecutor<TStreamId>(() => new TFReaderLease(readerPool)),
					unsafeIgnoreHardDeletes: _unsafeIgnoreHardDeletes,
					restPeriod: restPeriod,
					throttle: throttle);

				ICleaner cleaner = new Cleaner(logger, unsafeIgnoreHardDeletes: _unsafeIgnoreHardDeletes);

				accumulator = new TracingAccumulator<TStreamId>(accumulator, Tracer);
				calculator = new TracingCalculator<TStreamId>(calculator, Tracer);
				chunkExecutor = new TracingChunkExecutor<TStreamId>(chunkExecutor, Tracer);
				chunkMerger = new TracingChunkMerger(chunkMerger, Tracer);
				indexExecutor = new TracingIndexExecutor<TStreamId>(indexExecutor, Tracer);
				cleaner = new TracingCleaner(cleaner, Tracer);

				// if test provided its own logger it can check its own status
				var expectSuccess = _logger == null;
				var successLogger = expectSuccess ? new FakeTFScavengerLog() : null;

				sut = new Scavenger<TStreamId>(
					logger: logger,
					checkPreconditions: () => { },
					scavengeState,
					accumulator,
					calculator,
					chunkExecutor,
					chunkMerger,
					indexExecutor,
					cleaner,
					new MockScavengePointSource(
						dbResult,
						EffectiveNow,
						_newScavengePoint ?? new List<ScavengePoint>()),
					_logger ?? successLogger,
					statusTracker: new ScavengeStatusTracker.NoOp(),
					thresholdForNewScavenge: 0,
					syncOnly: _syncOnly,
					throttle.PrettyPrint);

				Tracer.Reset();
				await sut.ScavengeAsync(cancellationTokenSource.Token);

				// check if successful
				if (_logger == null) {
					Assert.True(successLogger.Completed);
					Assert.True(
						successLogger.Result == Core.TransactionLog.Chunks.ScavengeResult.Success,
						$"Status: {successLogger.Result}. Error: {successLogger.Error}");
				}

				// check the trace. only when Threads == 1 or the order isn't guaranteed.
				// only when not colliding everything, because the collisions will change the trace
				if (_expectedTrace != null && Threads == 1 && !CollideEverything) {
					var expected = _expectedTrace;
					var actual = Tracer.ToArray();
					for (var i = 0; i < Math.Max(expected.Length, actual.Length); i++) {

						if (expected[i] == Tracer.AnythingElse) {
							// actual can be anything it likes from this point on
							break;
						}

						var line = expected[i].Line;
						Assert.True(
							i < expected.Length,
							i < actual.Length
								? $"Actual trace contains extra entries starting with: {actual[i]}"
								: "impossible");

						Assert.True(
							i < actual.Length,
							$"Expected trace contains extra entries starting from line {line}: {expected[i].Message}");

						Assert.True(
							expected[i].Message == actual[i],
							$"Trace mismatch at line {line}. \r\n" +
							$" Expected: {expected[i].Message} \r\n" +
							$" Actual:   {actual[i]}");
					}
				}

				// See a list of the stream collisions
				//   - naively calculate list of collisions
				var hashesInUse = new Dictionary<ulong, TStreamId>();
				var collidingStreams = new HashSet<TStreamId>();

				void RegisterUse(TStreamId streamId) {
					var hash = hasher.Hash(streamId);
					if (hashesInUse.TryGetValue(hash, out var user)) {
						if (StreamIdComparer.Equals(user, streamId)) {
							// in use by us. not a collision.
						} else {
							// collision. register both as collisions.
							collidingStreams.Add(streamId);
							collidingStreams.Add(user);
						}
					} else {
						// hash was not in use. so it isn't a collision.
						hashesInUse[hash] = streamId;
					}
				}

				foreach (var chunk in dbResult.Recs) {
					foreach (var record in chunk) {
						if (record is not IPrepareLogRecord<TStreamId> prepare)
							continue;

						RegisterUse(prepare.EventStreamId);

						if (prepare.Flags.HasAnyOf(PrepareFlags.StreamDelete)) {
							RegisterUse(metastreamLookup.MetaStreamOf(prepare.EventStreamId));
						}

						if (metastreamLookup.IsMetaStream(prepare.EventStreamId)) {
							RegisterUse(metastreamLookup.OriginalStreamOf(prepare.EventStreamId));
						}
					}
				}

				if (CollideEverything) {
					// collidingStreams is all collisions in the log, not just up to the scavenge point
					// so can contains extra collisions, which becomes apparent when everything collides.
				} else {
					//   - Assert list of collisions.
					Assert.Equal(collidingStreams.OrderBy(x => x), scavengeState.AllCollisions().OrderBy(x => x));
				}

				// The records we expected to keep are kept
				// The index entries we expected to be kept are kept
				if (keptRecords != null) {
					CheckRecords(keptRecords, dbResult);
					CheckIndex(keptIndexEntries, readIndex, collidingStreams, hasher);
				}

				_assertState?.Invoke(scavengeState);

				return dbResult;

			} finally {
				sut?.Dispose();
				readIndex.Close();
				dbResult.Db.Close();
			}
		}

		// nicked from scavengetestscenario
		protected static void CheckRecords(ILogRecord[][] expected, DbResult actual) {
			Assert.True(
				expected.Length == actual.Db.Manager.ChunksCount,
				"Wrong number of chunks. " +
				$"Expected {expected.Length}. Actual {actual.Db.Manager.ChunksCount}");

			for (int i = 0; i < expected.Length; ++i) {
				var chunk = actual.Db.Manager.GetChunk(i);

				var chunkRecords = new List<ILogRecord>();
				var result = chunk.TryReadFirst();
				while (result.Success) {
					chunkRecords.Add(result.LogRecord);
					result = chunk.TryReadClosestForward((int)result.NextPosition);
				}

				Assert.True(
					expected[i].Length == chunkRecords.Count,
					$"Wrong number of records in chunk #{i}. " +
					$"Expected {expected[i].Length}. Actual {chunkRecords.Count}");

				for (int j = 0; j < expected[i].Length; ++j) {
					Assert.True(
						expected[i][j].Equals(chunkRecords[j]),
						$"Wrong log record #{j} read from chunk #{i}. " +
						$"Expected {expected[i][j]}.\r\n" +
						$"Actual   {chunkRecords[j]}");
				}
			}
		}

		// we want to check that the index contains everything it is supposed to
		// and we want to check that the index doesn't contain anything extra.
		private static void CheckIndex(
			ILogRecord[][] expected,
			IReadIndex<TStreamId> actual,
			HashSet<TStreamId> collisions,
			ILongHasher<TStreamId> hasher) {

			if (expected == null) {
				// test didn't ask us to check the index
				return;
			}

			// check we have everything we are supposed to
			// cant use normal stream reads because they will apply metadata etc.
			var minEventNumbers = new Dictionary<TStreamId, long>();
			var maxEventNumbers = new Dictionary<TStreamId, long>();

			foreach (var chunk in expected) {
				foreach (var record in chunk) {
					if (record is not IPrepareLogRecord<TStreamId> prepare)
						throw new Exception("expected to find commit record in index but this is impossible");

					var streamId = prepare.EventStreamId;
					var eventNumber = prepare.ExpectedVersion + 1;

					// indexcommitter blesses tombstones with EventNumber.DeletedStream when they are
					// committed with an explicit commit record
					if (prepare.Flags.HasAnyOf(PrepareFlags.StreamDelete) &&
						prepare.Flags.HasNoneOf(PrepareFlags.IsCommitted))
						eventNumber = EventNumber.DeletedStream;

					if (!minEventNumbers.TryGetValue(streamId, out var min))
						min = eventNumber;
					minEventNumbers[streamId] = Math.Min(eventNumber, min);

					if (!maxEventNumbers.TryGetValue(streamId, out var max))
						max = eventNumber;
					maxEventNumbers[streamId] = Math.Max(eventNumber, max);

					var result = collisions.Contains(streamId)
						? actual.ReadEventInfoForward_KnownCollisions(
							streamId: streamId,
							fromEventNumber: eventNumber,
							maxCount: 1,
							beforePosition: long.MaxValue)
						: actual.ReadEventInfoForward_NoCollisions(
							stream: hasher.Hash(streamId),
							fromEventNumber: eventNumber,
							maxCount: 1,
						beforePosition: long.MaxValue);

					if (result.EventInfos.Length != 1) {
						// remember this applies metadata, so is of limited use
						var wholeStream = actual.ReadStreamEventsForward($"{streamId}", streamId, fromEventNumber: 0, maxCount: 100);
						Assert.True(result.EventInfos.Length == 1, $"Couldn't find {streamId}:{eventNumber} in index.");
					}


					var info = result.EventInfos[0];
					Assert.Equal(prepare.LogPosition, info.LogPosition);
					Assert.Equal(eventNumber, info.EventNumber);
				}
			}
		
			// check we don't have anything extra
			// (we can't easily check that there aren't unexpected streams in the index, but risk of this
			// is low)
			// nothing before the min, or after the max that we found in the log.
			foreach (var kvp in minEventNumbers) {
				var streamId = kvp.Key;
				var min = kvp.Value;
				var max = maxEventNumbers[streamId];

				var result = collisions.Contains(streamId)
					? actual.ReadEventInfoForward_KnownCollisions(
						streamId: streamId,
						fromEventNumber: 0,
						maxCount: 1000,
						beforePosition: long.MaxValue)
					: actual.ReadEventInfoForward_NoCollisions(
						stream: hasher.Hash(streamId),
						fromEventNumber: 0,
						maxCount: 1000,
						beforePosition: long.MaxValue);

				if (result.EventInfos.Length > 100)
					throw new Exception("wasn't expecting a stream this long in the tests");

				Assert.All(result.EventInfos, info => {
					Assert.True(info.EventNumber >= min);
					Assert.True(info.EventNumber <= max);
				});
			}
		}

		private void EmptyRequestedChunks(TFChunkDb db) {
			foreach (var chunkNum in _chunkNumsToEmpty) {
				var chunk = db.Manager.GetChunk(chunkNum);
				var header = chunk.ChunkHeader;

				var newChunkHeader = new ChunkHeader(
					version: header.Version,
					chunkSize: header.ChunkSize,
					chunkStartNumber: header.ChunkStartNumber,
					chunkEndNumber: header.ChunkEndNumber,
					isScavenged: true,
					chunkId: Guid.NewGuid());

				var newChunk = TFChunk.CreateWithHeader(
					new UnmanagedChunkCacheManager(),
					filename: $"{chunk.FileName}.tmp",
					header: newChunkHeader,
					fileSize: ChunkHeader.Size,
					inMem: false,
					unbuffered: false,
					writethrough: false,
					initialReaderCount: 1,
					maxReaderCount: 1,
					reduceFileCachePressure: false,
					new TFChunkTracker.NoOp());

				newChunk.CompleteScavenge(null);

				db.Manager.SwitchChunk(newChunk, false, false);
			}
		}
	}
}
