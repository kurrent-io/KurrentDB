using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Scavenging {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_stream_is_deleted<TLogFormat, TStreamId> : ScavengeTestScenario<TLogFormat, TStreamId> {
		protected override DbResult CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator) {
			return dbCreator
				.Chunk(Rec.Prepare(0, "bla"), Rec.Prepare(0, "bla"), Rec.Commit(0, "bla"))
				.Chunk(Rec.Delete(1, "bla"), Rec.Commit(1, "bla"))
				.CompleteLastChunk()
				.CreateDb();
		}

		protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
			if (LogFormatHelper<TLogFormat, TStreamId>.IsV2) {
				return new[] {
					new ILogRecord[0],
					dbResult.Recs[1]
				};
			}

			return new[] {
				new[] {
					dbResult.Recs[0][0], // "bla" created
				},
				dbResult.Recs[1]
			};
		}

		[Test]
		public void stream_created_and_delete_tombstone_with_corresponding_commits_are_kept() {
			CheckRecords();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_stream_is_deleted_with_ignore_hard_deletes<TLogFormat, TStreamId> : ScavengeTestScenario<TLogFormat, TStreamId> {
		protected override bool UnsafeIgnoreHardDelete() {
			return true;
		}

		protected override DbResult CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator) {
			return dbCreator
				.Chunk(Rec.Prepare(0, "bla"), Rec.Prepare(0, "bla"), Rec.Commit(0, "bla"))
				.Chunk(Rec.Delete(1, "bla"), Rec.Commit(1, "bla"))
				.CompleteLastChunk()
				.CreateDb();
		}

		protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
			if (LogFormatHelper<TLogFormat, TStreamId>.IsV2) {
				return new[] {
					new ILogRecord[0],
					new ILogRecord[0]
				};
			}

			return new[] {
				new[] {
					dbResult.Recs[0][0], // "bla" created
				},
				new ILogRecord[0]
			};
		}

		[Test]
		public void stream_created_and_delete_tombstone_with_corresponding_commits_are_kept() {
			CheckRecords();
		}
	}
}

