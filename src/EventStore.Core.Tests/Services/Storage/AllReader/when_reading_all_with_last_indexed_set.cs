using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.Tests.Services.Storage.AllReader {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_reading_all<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {

		protected override void WriteTestScenario() {
			WritePrepare("ES1", 0, Guid.NewGuid(), "event-type", new string('.', 3000), PrepareFlags.IsCommitted);
			WritePrepare("ES2", 0, Guid.NewGuid(), "event-type", new string('.', 3000), PrepareFlags.IsCommitted);
			WritePrepare("ES2", 1, Guid.NewGuid(), "event-type", new string('.', 3000), PrepareFlags.IsCommitted);
		}

		[Test]
		public async Task should_be_able_to_read_all_backwards() {
			var checkpoint = WriterCheckpoint.Read();
			var pos = new TFPos(checkpoint, checkpoint);
			var result = (await ReadIndex.ReadAllEventsBackward(pos, 10, CancellationToken.None)).EventRecords();
			Assert.AreEqual(3, result.Count);
		}

		[Test]
		public void should_be_able_to_read_all_forwards() {
			var result = ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 10).EventRecords();
			Assert.AreEqual(3, result.Count);
		}
	}
}
