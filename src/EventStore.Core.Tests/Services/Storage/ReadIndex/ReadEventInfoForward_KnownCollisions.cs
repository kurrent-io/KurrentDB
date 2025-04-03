// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Index.Hashers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.ReadIndex;

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
		[Test]
		public void verify_that_streams_collide() {
			Assert.AreEqual(Hasher.Hash(Stream), Hasher.Hash(CollidingStream));
			Assert.AreEqual(Hasher.Hash(Stream), Hasher.Hash(CollidingStream1));
		}
	}

	public class WithNoEvents : ReadEventInfoForward_KnownCollisions {
		protected override async ValueTask WriteTestScenario(CancellationToken token) {
			await WriteSingleEvent(CollidingStream, 0, "test data", token: token);
			await WriteSingleEvent(CollidingStream1, 0, "test data", token: token);
		}

		[Test]
		public async Task with_no_events() {
			var result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				0,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

			Assert.AreEqual(0, result.EventInfos.Length);
			Assert.True(result.IsEndOfStream);
		}
	}

	public class WithOneEvent : ReadEventInfoForward_KnownCollisions {
		private EventRecord _event, _collidingEvent;

		protected override async ValueTask WriteTestScenario(CancellationToken token) {
			_event = await WriteSingleEvent(Stream, 0, "test data", token: token);
			_collidingEvent = await WriteSingleEvent(CollidingStream, 0, "test data", token: token);
		}

		[Test]
		public async Task with_one_event() {
			var result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				0,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

			Assert.AreEqual(1, result.EventInfos.Length);
			CheckResult(new[] { _event }, result);
			Assert.AreEqual(int.MaxValue, result.NextEventNumber);

			result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				1,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

			CheckResult(new EventRecord[] { }, result);
			Assert.True(result.IsEndOfStream);

			result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				CollidingStream,
				0,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

			Assert.AreEqual(1, result.EventInfos.Length);
			CheckResult(new[] { _collidingEvent }, result);
			Assert.AreEqual(int.MaxValue, result.NextEventNumber);

			result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				CollidingStream,
				1,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

			CheckResult(new EventRecord[] { }, result);
			Assert.True(result.IsEndOfStream);
		}
	}

	public class WithMultipleEvents : ReadEventInfoForward_KnownCollisions {
		private readonly List<EventRecord> _events = new();

		protected override async ValueTask WriteTestScenario(CancellationToken token) {
			// PTable 1
			await WriteSingleEvent(CollidingStream, 0, string.Empty);
			await WriteSingleEvent(CollidingStream, 1, string.Empty);
			_events.Add(await WriteSingleEvent(Stream, 0, string.Empty));

			// PTable 2
			_events.Add(await WriteSingleEvent(Stream, 1, string.Empty, token: token));
			_events.Add(await WriteSingleEvent(Stream, 2, string.Empty, token: token));
			await WriteSingleEvent(CollidingStream, 2, string.Empty, token: token);

			// MemTable
			_events.Add(await WriteSingleEvent(Stream, 3, string.Empty, token: token));
			await WriteSingleEvent(CollidingStream, 3, string.Empty, token: token);
		}

		[Test]
		public async Task with_multiple_events() {
			for (int fromEventNumber = 0; fromEventNumber <= 4; fromEventNumber++) {
				var result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					fromEventNumber,
					int.MaxValue,
					long.MaxValue,
					CancellationToken.None);

				CheckResult(_events.Skip(fromEventNumber).ToArray(), result);
				if (fromEventNumber > 3)
					Assert.True(result.IsEndOfStream);
				else
					Assert.AreEqual((long) fromEventNumber + int.MaxValue, result.NextEventNumber);
			}
		}

		[Test]
		public async Task with_multiple_events_and_max_count() {
			for (int fromEventNumber = 0; fromEventNumber <= 4; fromEventNumber++) {
				var result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					fromEventNumber,
					2,
					long.MaxValue,
					CancellationToken.None);

				CheckResult(_events.Skip(fromEventNumber).Take(2).ToArray(), result);
				if (fromEventNumber > 3)
					Assert.True(result.IsEndOfStream);
				else
					Assert.AreEqual((long) fromEventNumber + 2, result.NextEventNumber);
			}
		}

		[Test]
		public async Task with_multiple_events_and_before_position() {
			for (int fromEventNumber = 0; fromEventNumber + 1 < _events.Count; fromEventNumber++) {
				var result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
					Stream,
					fromEventNumber,
					int.MaxValue,
					_events[fromEventNumber + 1].LogPosition,
					CancellationToken.None);

				CheckResult(_events.Skip(fromEventNumber).Take(1).ToArray(), result);
				Assert.AreEqual((long) fromEventNumber + int.MaxValue, result.NextEventNumber);
			}
		}
	}

	public class WithDeletedStream : ReadEventInfoForward_KnownCollisions {
		private readonly List<EventRecord> _events = new();

		protected override async ValueTask WriteTestScenario(CancellationToken token) {
			_events.Add(await WriteSingleEvent(Stream, 0, "test data", token: token));
			await WriteSingleEvent(CollidingStream, 1, "test data", token: token);
			_events.Add(await WriteSingleEvent(Stream, 1, "test", token: token));

			var prepare = await WriteDeletePrepare(Stream, token);
			await WriteDeleteCommit(prepare, token);
		}

		[Test]
		public async Task can_read_events_and_tombstone_event_not_returned() {
			var result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				0,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

			CheckResult(_events.ToArray(), result);
			Assert.AreEqual(int.MaxValue, result.NextEventNumber);
		}

		[Test]
		public async Task next_event_number_set_correctly() {
			var result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				2,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

			Assert.AreEqual(0, result.EventInfos.Length);
			Assert.AreEqual(EventNumber.DeletedStream, result.NextEventNumber);
		}

		[Test]
		public async Task can_read_tombstone_event() {
			var result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				EventNumber.DeletedStream,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

			Assert.AreEqual(1, result.EventInfos.Length);
			Assert.AreEqual(EventNumber.DeletedStream, result.EventInfos[0].EventNumber);
			Assert.True(result.IsEndOfStream);
		}
	}

	public class WithGapsBetweenEvents : ReadEventInfoForward_KnownCollisions {
		private readonly List<EventRecord> _events = new();

		protected override async ValueTask WriteTestScenario(CancellationToken token) {
			// PTable 1
			await WriteSingleEvent(CollidingStream, 0, string.Empty, token: token);
			await WriteSingleEvent(CollidingStream, 1, string.Empty, token: token);
			_events.Add(await WriteSingleEvent(Stream, 0, string.Empty, token: token));

			// PTable 2
			_events.Add(await WriteSingleEvent(Stream, 4, string.Empty, token: token));
			_events.Add(await WriteSingleEvent(Stream, 5, string.Empty, token: token));
			await WriteSingleEvent(CollidingStream, 2, string.Empty, token: token);

			// MemTable
			_events.Add(await WriteSingleEvent(Stream, 11, string.Empty, token: token));
			await WriteSingleEvent(CollidingStream, 3, string.Empty, token: token);
			await WriteSingleEvent(CollidingStream1, 15, string.Empty, token: token);
		}

		[Test]
		public async Task strictly_returns_up_to_max_count_consecutive_events_from_start_event_number() {
			var result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				0,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

			CheckResult(_events.ToArray(), result);
			Assert.AreEqual(int.MaxValue, result.NextEventNumber);

			result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				0,
				3,
				long.MaxValue,
				CancellationToken.None);

			CheckResult(_events.Take(1).ToArray(), result);
			Assert.AreEqual(3, result.NextEventNumber);

			result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				3,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

			CheckResult(_events.Skip(1).ToArray(), result);
			Assert.AreEqual((long ) 3 + int.MaxValue, result.NextEventNumber);

			result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				4,
				3,
				long.MaxValue,
				CancellationToken.None);

			CheckResult(_events.Skip(1).Take(2).ToArray(), result);
			Assert.AreEqual(7, result.NextEventNumber);

			result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				7,
				3,
				long.MaxValue,
				CancellationToken.None);

			Assert.AreEqual(0, result.EventInfos.Length);
			Assert.AreEqual(11, result.NextEventNumber);

			result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				12,
				1,
				long.MaxValue,
				CancellationToken.None);

			Assert.AreEqual(0, result.EventInfos.Length);
			Assert.AreEqual(15, result.NextEventNumber); // from colliding stream, but doesn't matter much

			result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				12,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

			Assert.AreEqual(0, result.EventInfos.Length);
			Assert.True(result.IsEndOfStream);
		}
	}


	public class WithDuplicateEvents : ReadEventInfoForward_KnownCollisions {
		private readonly List<EventRecord> _events = new();

		protected override async ValueTask WriteTestScenario(CancellationToken token) {
			// PTable 1
			await WriteSingleEvent(CollidingStream, 0, string.Empty, token: token);
			await WriteSingleEvent(CollidingStream, 1, string.Empty, token: token);
			_events.Add(await WriteSingleEvent(Stream, 0, string.Empty, token: token));

			// PTable 2
			_events.Add(await WriteSingleEvent(Stream, 1, string.Empty, token: token));
			_events.Add(await WriteSingleEvent(Stream, 1, string.Empty, token: token));
			await WriteSingleEvent(CollidingStream, 2, string.Empty, token: token);

			// MemTable
			_events.Add(await WriteSingleEvent(Stream, 2, string.Empty, token: token));
			_events.Add(await WriteSingleEvent(Stream, 2, string.Empty, token: token));
			_events.Add(await WriteSingleEvent(Stream, 2, string.Empty, token: token));
		}

		[Test]
		public async Task result_is_deduplicated_keeping_oldest_duplicates() {
			var result = await ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream,
				0,
				int.MaxValue,
				long.MaxValue,
				CancellationToken.None);

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
