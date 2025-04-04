using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EventStore.Core.Data.Redaction;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using FluentAssertions;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RedactionService {

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class EventPositionTests<TLogFormat, TStreamId> : RedactionServiceTestFixture<TLogFormat, TStreamId> {
		private const string StreamId = nameof(EventPositionTests<TLogFormat, TStreamId>);
		private readonly Dictionary<long, List<EventPosition>> _positions = new();

		private void WriteEvent(string streamId, long eventNumber, string data) {
			var eventRecord = WriteSingleEvent(streamId, eventNumber, data);
			if (!_positions.ContainsKey(eventNumber))
				_positions[eventNumber] = new();

			var chunk = Db.Manager.GetChunkFor(eventRecord.LogPosition);
			var eventOffset = chunk.GetActualRawPosition(eventRecord.LogPosition);
			var eventPosition = new EventPosition(
				eventRecord.LogPosition, Path.GetFileName(chunk.FileName), chunk.ChunkHeader.Version, chunk.IsReadOnly, (uint)eventOffset);
			_positions[eventNumber].Add(eventPosition);
		}

		protected override void WriteTestScenario() {
			WriteEvent(StreamId, 2, "data 2");
			WriteEvent(StreamId, 0, "data 0");
			WriteEvent(StreamId, 1, "data 1");
			WriteEvent(StreamId, 2, "data 2"); // duplicate
		}

		private async Task<RedactionMessage.GetEventPositionCompleted> GetEventPosition(long eventNumber) {
			var e = new TcsEnvelope<RedactionMessage.GetEventPositionCompleted>();
			RedactionService.Handle(new RedactionMessage.GetEventPosition(e, StreamId, eventNumber));
			return await e.Task;
		}

		[Test]
		public async Task can_get_positions_of_event_0() {
			var msg = await GetEventPosition(0);
			Assert.AreEqual(GetEventPositionResult.Success, msg.Result);
			Assert.AreEqual(1, msg.EventPositions.Length);
			msg.EventPositions.Should().BeEquivalentTo(_positions[0].ToArray());
		}

		[Test]
		public async Task can_get_positions_of_event_1() {
			var msg = await GetEventPosition(1);
			Assert.AreEqual(GetEventPositionResult.Success, msg.Result);
			Assert.AreEqual(1, msg.EventPositions.Length);
			msg.EventPositions.Should().BeEquivalentTo(_positions[1].ToArray());
		}

		[Test]
		public async Task can_get_positions_of_event_2() {
			var msg = await GetEventPosition(2);
			Assert.AreEqual(GetEventPositionResult.Success, msg.Result);
			Assert.AreEqual(2, msg.EventPositions.Length);
			msg.EventPositions.Should().BeEquivalentTo(_positions[2].ToArray());
		}
	}
}
