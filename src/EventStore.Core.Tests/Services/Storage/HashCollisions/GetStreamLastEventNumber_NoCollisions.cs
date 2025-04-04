// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Tests.Index.Hashers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.HashCollisions;

[TestFixture]
public abstract class GetStreamLastEventNumber_NoCollisions : ReadIndexTestScenario<LogFormat.V2, string> {
	private const string Stream = "ab-1";
	private const ulong Hash = 98;
	private const string NonCollidingStream = "cd-1";

	private string GetStreamId(ulong hash) => hash == Hash ? Stream : throw new ArgumentException();

	protected GetStreamLastEventNumber_NoCollisions() : base(
		maxEntriesInMemTable: 3,
		lowHasher: new ConstantHasher(0),
		highHasher: new HumanReadableHasher32()) { }

	public class VerifyNoCollision : GetStreamLastEventNumber_NoCollisions {
		[Test]
		public void verify_that_streams_do_not_collide() {
			Assert.AreNotEqual(Hasher.Hash(Stream), Hasher.Hash(NonCollidingStream));
		}
	}

	public class WithNoEvents : GetStreamLastEventNumber_NoCollisions {
		[Test]
		public async Task with_no_events() {
			Assert.AreEqual(ExpectedVersion.NoStream,
				await ReadIndex.GetStreamLastEventNumber_NoCollisions(
					Hash,
					GetStreamId,
					long.MaxValue,
					CancellationToken.None));
		}
	}

	public class WithOneEvent : GetStreamLastEventNumber_NoCollisions {
		protected override async ValueTask WriteTestScenario(CancellationToken token) {
			await WriteSingleEvent(Stream, 0, "test data", token: token);
		}

		[Test]
		public async Task with_one_event() {
			Assert.AreEqual(0,
				await ReadIndex.GetStreamLastEventNumber_NoCollisions(
					Hash,
					GetStreamId,
					long.MaxValue,
					CancellationToken.None));
		}
	}

	public class WithMultipleEvents : GetStreamLastEventNumber_NoCollisions {
		private EventRecord _zeroth, _first, _second, _third;

		protected override async ValueTask WriteTestScenario(CancellationToken token) {
			// PTable 1
			await WriteSingleEvent(NonCollidingStream, 0, string.Empty, token: token);
			await WriteSingleEvent(NonCollidingStream, 1, string.Empty, token: token);
			_zeroth = await WriteSingleEvent(Stream, 0, string.Empty, token: token);

			// PTable 2
			_first = await WriteSingleEvent(Stream, 1, string.Empty, token: token);
			_second = await WriteSingleEvent(Stream, 2, string.Empty, token: token);
			await WriteSingleEvent(NonCollidingStream, 2, string.Empty, token: token);

			// MemTable
			_third = await WriteSingleEvent(Stream, 3, string.Empty, token: token);
			await WriteSingleEvent(NonCollidingStream, 3, string.Empty, token: token);
		}

		[Test]
		public async Task with_multiple_events() {
			Assert.AreEqual(3,
				await ReadIndex.GetStreamLastEventNumber_NoCollisions(
					Hash,
					GetStreamId,
					long.MaxValue,
					CancellationToken.None));
		}

		[Test]
		public async Task with_multiple_events_and_before_position() {
			Assert.AreEqual(3,
				await ReadIndex.GetStreamLastEventNumber_NoCollisions(
					Hash,
					GetStreamId,
					_third.LogPosition + 1,
					CancellationToken.None));

			Assert.AreEqual(2,
				await ReadIndex.GetStreamLastEventNumber_NoCollisions(
					Hash,
					GetStreamId,
					_third.LogPosition,
					CancellationToken.None));

			Assert.AreEqual(1,
				await ReadIndex.GetStreamLastEventNumber_NoCollisions(
					Hash,
					GetStreamId,
					_second.LogPosition,
					CancellationToken.None));

			Assert.AreEqual(0,
				await ReadIndex.GetStreamLastEventNumber_NoCollisions(
					Hash,
					GetStreamId,
					_first.LogPosition,
					CancellationToken.None));

			Assert.AreEqual(ExpectedVersion.NoStream,
				await ReadIndex.GetStreamLastEventNumber_NoCollisions(
					Hash,
					GetStreamId,
					_zeroth.LogPosition,
					CancellationToken.None));
		}
	}

	public class WithDeletedStream : GetStreamLastEventNumber_NoCollisions {
		protected override async ValueTask WriteTestScenario(CancellationToken token) {
			await WriteSingleEvent(Stream, 0, "test data", token: token);
			await WriteSingleEvent(Stream, 1, "test data", token: token);

			var prepare = await WriteDeletePrepare(Stream, token);
			await WriteDeleteCommit(prepare, token);
		}

		[Test]
		public async Task with_deleted_stream() {
			Assert.AreEqual(EventNumber.DeletedStream,
				await ReadIndex.GetStreamLastEventNumber_NoCollisions(
					Hash,
					GetStreamId,
					long.MaxValue,
					CancellationToken.None));
		}
	}
}
