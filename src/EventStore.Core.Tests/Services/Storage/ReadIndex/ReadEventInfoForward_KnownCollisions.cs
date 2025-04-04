using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Index.Hashers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.ReadIndex {
	[TestFixture]
	public abstract class ReadEventInfoForward_KnownCollisions : ReadIndexTestScenario<LogFormat.V2, string> {
		private const string Stream = "ab-1";
		private const string CollidingStream = "cb-1";
		private const string CollidingStream1 = "db-1";

		protected ReadEventInfoForward_KnownCollisions() : base(
			maxEntriesInMemTable: 3,
			lowHasher: new ConstantHasher(0),
			highHasher: new HumanReadableHasher32()) { }

		private static void CheckResult(EventRecord[] events, IndexReadEventInfoResult result) {
			Assert.AreEqual(events.Length, result.EventInfos.Length);
			for (int i = 0; i < events.Length; i++) {
				Assert.AreEqual(events[i].EventNumber, result.EventInfos[i].EventNumber);
				Assert.AreEqual(events[i].LogPosition, result.EventInfos[i].LogPosition);
			}
		}

		public class VerifyCollision : ReadEventInfoForward_KnownCollisions {
			protected override void WriteTestScenario() { }

			[Test]
			public void verify_that_streams_collide() {
				Assert.AreEqual(Hasher.Hash(Stream), Hasher.Hash(CollidingStream));
				Assert.AreEqual(Hasher.Hash(Stream), Hasher.Hash(CollidingStream1));
			}
		}

		public class WithNoEvents : ReadEventInfoForward_KnownCollisions {
			protected override void WriteTestScenario() {
				WriteSingleEvent(CollidingStream, 0, "test data");
				WriteSingleEvent(CollidingStream1, 0, "test data");
			}

			[Test]
			public void with_no_events() {
				var result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					0,
					int.MaxValue,
					long.MaxValue);

				Assert.AreEqual(0, result.EventInfos.Length);
				Assert.True(result.IsEndOfStream);
			}
		}

		public class WithOneEvent : ReadEventInfoForward_KnownCollisions {
			private EventRecord _event, _collidingEvent;

			protected override void WriteTestScenario() {
				_event = WriteSingleEvent(Stream, 0, "test data");
				_collidingEvent = WriteSingleEvent(CollidingStream, 0, "test data");
			}

			[Test]
			public void with_one_event() {
				var result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					0,
					int.MaxValue,
					long.MaxValue);

				Assert.AreEqual(1, result.EventInfos.Length);
				CheckResult(new[] { _event }, result);
				Assert.AreEqual(int.MaxValue, result.NextEventNumber);

				result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					1,
					int.MaxValue,
					long.MaxValue);

				CheckResult(new EventRecord[] { }, result);
				Assert.True(result.IsEndOfStream);

				result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					CollidingStream,
					0,
					int.MaxValue,
					long.MaxValue);

				Assert.AreEqual(1, result.EventInfos.Length);
				CheckResult(new[] { _collidingEvent }, result);
				Assert.AreEqual(int.MaxValue, result.NextEventNumber);

				result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					CollidingStream,
					1,
					int.MaxValue,
					long.MaxValue);

				CheckResult(new EventRecord[] { }, result);
				Assert.True(result.IsEndOfStream);
			}
		}

		public class WithMultipleEvents : ReadEventInfoForward_KnownCollisions {
			private readonly List<EventRecord> _events = new List<EventRecord>();

			protected override void WriteTestScenario() {
				// PTable 1
				WriteSingleEvent(CollidingStream, 0, string.Empty);
				WriteSingleEvent(CollidingStream, 1, string.Empty);
				_events.Add(WriteSingleEvent(Stream, 0, string.Empty));

				// PTable 2
				_events.Add(WriteSingleEvent(Stream, 1, string.Empty));
				_events.Add(WriteSingleEvent(Stream, 2, string.Empty));
				WriteSingleEvent(CollidingStream, 2, string.Empty);

				// MemTable
				_events.Add(WriteSingleEvent(Stream, 3, string.Empty));
				WriteSingleEvent(CollidingStream, 3, string.Empty);
			}

			[Test]
			public void with_multiple_events() {
				for (int fromEventNumber = 0; fromEventNumber <= 4; fromEventNumber++) {
					var result = ReadIndex.ReadEventInfoForward_KnownCollisions(
						Stream,
						fromEventNumber,
						int.MaxValue,
						long.MaxValue);

					CheckResult(_events.Skip(fromEventNumber).ToArray(), result);
					if (fromEventNumber > 3)
						Assert.True(result.IsEndOfStream);
					else
						Assert.AreEqual((long)fromEventNumber + int.MaxValue, result.NextEventNumber);
				}
			}

			[Test]
			public void with_multiple_events_and_max_count() {
				for (int fromEventNumber = 0; fromEventNumber <= 4; fromEventNumber++) {
					var result = ReadIndex.ReadEventInfoForward_KnownCollisions(
						Stream,
						fromEventNumber,
						2,
						long.MaxValue);

					CheckResult(_events.Skip(fromEventNumber).Take(2).ToArray(), result);
					if (fromEventNumber > 3)
						Assert.True(result.IsEndOfStream);
					else
						Assert.AreEqual((long)fromEventNumber + 2, result.NextEventNumber);
				}
			}

			[Test]
			public void with_multiple_events_and_before_position() {
				for (int fromEventNumber = 0; fromEventNumber + 1 < _events.Count; fromEventNumber++) {
					var result = ReadIndex.ReadEventInfoForward_KnownCollisions(
						Stream,
						fromEventNumber,
						int.MaxValue,
						_events[fromEventNumber + 1].LogPosition);

					CheckResult(_events.Skip(fromEventNumber).Take(1).ToArray(), result);
					Assert.AreEqual((long)fromEventNumber + int.MaxValue, result.NextEventNumber);
				}
			}
		}

		public class WithDeletedStream : ReadEventInfoForward_KnownCollisions {
			private readonly List<EventRecord> _events = new List<EventRecord>();

			protected override void WriteTestScenario() {
				_events.Add(WriteSingleEvent(Stream, 0, "test data"));
				WriteSingleEvent(CollidingStream, 1, "test data");
				_events.Add(WriteSingleEvent(Stream, 1, "test"));

				var prepare = WriteDeletePrepare(Stream);
				WriteDeleteCommit(prepare);
			}

			[Test]
			public void can_read_events_and_tombstone_event_not_returned() {
				var result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					0,
					int.MaxValue,
					long.MaxValue);

				CheckResult(_events.ToArray(), result);
				Assert.AreEqual(int.MaxValue, result.NextEventNumber);
			}

			[Test]
			public void next_event_number_set_correctly() {
				var result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					2,
					int.MaxValue,
					long.MaxValue);

				Assert.AreEqual(0, result.EventInfos.Length);
				Assert.AreEqual(EventNumber.DeletedStream, result.NextEventNumber);
			}

			[Test]
			public void can_read_tombstone_event() {
				var result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					EventNumber.DeletedStream,
					int.MaxValue,
					long.MaxValue);

				Assert.AreEqual(1, result.EventInfos.Length);
				Assert.AreEqual(EventNumber.DeletedStream, result.EventInfos[0].EventNumber);
				Assert.True(result.IsEndOfStream);
			}
		}

		public class WithGapsBetweenEvents : ReadEventInfoForward_KnownCollisions {
			private readonly List<EventRecord> _events = new List<EventRecord>();

			protected override void WriteTestScenario() {
				// PTable 1
				WriteSingleEvent(CollidingStream, 0, string.Empty);
				WriteSingleEvent(CollidingStream, 1, string.Empty);
				_events.Add(WriteSingleEvent(Stream, 0, string.Empty));

				// PTable 2
				_events.Add(WriteSingleEvent(Stream, 4, string.Empty));
				_events.Add(WriteSingleEvent(Stream, 5, string.Empty));
				WriteSingleEvent(CollidingStream, 2, string.Empty);

				// MemTable
				_events.Add(WriteSingleEvent(Stream, 11, string.Empty));
				WriteSingleEvent(CollidingStream, 3, string.Empty);
				WriteSingleEvent(CollidingStream1, 15, string.Empty);
			}

			[Test]
			public void strictly_returns_up_to_max_count_consecutive_events_from_start_event_number() {
				var result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					0,
					int.MaxValue,
					long.MaxValue);

				CheckResult(_events.ToArray(), result);
				Assert.AreEqual(int.MaxValue, result.NextEventNumber);

				result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					0,
					3,
					long.MaxValue);

				CheckResult(_events.Take(1).ToArray(), result);
				Assert.AreEqual(3, result.NextEventNumber);

				result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					3,
					int.MaxValue,
					long.MaxValue);

				CheckResult(_events.Skip(1).ToArray(), result);
				Assert.AreEqual((long)3 + int.MaxValue, result.NextEventNumber);

				result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					4,
					3,
					long.MaxValue);

				CheckResult(_events.Skip(1).Take(2).ToArray(), result);
				Assert.AreEqual(7, result.NextEventNumber);

				result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					7,
					3,
					long.MaxValue);

				Assert.AreEqual(0, result.EventInfos.Length);
				Assert.AreEqual(11, result.NextEventNumber);

				result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					12,
					1,
					long.MaxValue);

				Assert.AreEqual(0, result.EventInfos.Length);
				Assert.AreEqual(15, result.NextEventNumber); // from colliding stream, but doesn't matter much

				result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					12,
					int.MaxValue,
					long.MaxValue);

				Assert.AreEqual(0, result.EventInfos.Length);
				Assert.True(result.IsEndOfStream);
			}
		}


		public class WithDuplicateEvents : ReadEventInfoForward_KnownCollisions {
			private readonly List<EventRecord> _events = new List<EventRecord>();

			protected override void WriteTestScenario() {
				// PTable 1
				WriteSingleEvent(CollidingStream, 0, string.Empty);
				WriteSingleEvent(CollidingStream, 1, string.Empty);
				_events.Add(WriteSingleEvent(Stream, 0, string.Empty));

				// PTable 2
				_events.Add(WriteSingleEvent(Stream, 1, string.Empty));
				_events.Add(WriteSingleEvent(Stream, 1, string.Empty));
				WriteSingleEvent(CollidingStream, 2, string.Empty);

				// MemTable
				_events.Add(WriteSingleEvent(Stream, 2, string.Empty));
				_events.Add(WriteSingleEvent(Stream, 2, string.Empty));
				_events.Add(WriteSingleEvent(Stream, 2, string.Empty));
			}

			[Test]
			public void result_is_deduplicated_keeping_oldest_duplicates() {
				var result = ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					0,
					int.MaxValue,
					long.MaxValue);

				CheckResult(
					_events
						.GroupBy(x => x.EventNumber)
						.Select(x => x.First())
						.OrderBy(x => x.EventNumber)
						.ToArray(),
					result);
				Assert.AreEqual(int.MaxValue, result.NextEventNumber);
			}
		}
	}

}
