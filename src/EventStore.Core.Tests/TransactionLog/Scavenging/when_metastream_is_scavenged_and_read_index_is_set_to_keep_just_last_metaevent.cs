using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Scavenging {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_metastream_is_scavenged_and_read_index_is_set_to_keep_just_last_metaevent<TLogFormat, TStreamId> : ScavengeTestScenario<TLogFormat, TStreamId> {
		protected override DbResult CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator) {
			return dbCreator
				.Chunk(Rec.Prepare(0, "$$bla", metadata: new StreamMetadata(10, null, null, null, null)),
					Rec.Prepare(0, "$$bla", metadata: new StreamMetadata(5, null, null, null, null)),
					Rec.Prepare(0, "$$bla", metadata: new StreamMetadata(3, null, null, null, null)),
					Rec.Prepare(0, "$$bla", metadata: new StreamMetadata(2, null, null, null, null)),
					Rec.Commit(0, "$$bla"))
				.CompleteLastChunk()
				.CreateDb();
		}

		protected override ILogRecord[][] KeptRecords(DbResult dbResult) {
			return LogFormatHelper<TLogFormat, TStreamId>.IsV2
				? new[] { dbResult.Recs[0].Where((x, i) => i >= 3).ToArray() }
				: new[] { dbResult.Recs[0].Where((x, i) => i == 0 || i >= 4).ToArray() };
		}

		[Test]
		public void only_last_metaevent_is_left() {
			CheckRecords();
		}
	}
}
