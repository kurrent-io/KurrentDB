// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Transport.Enumerators;

[TestFixture]
public partial class EnumeratorTests {
	private static EnumeratorWrapper CreateAllSubscriptionFiltered(
		IPublisher publisher,
		Position? checkpoint,
		IEventFilter eventFilter = null,
		uint? maxSearchWindow = null,
		uint checkpointIntervalMultiplier = 1,
		ClaimsPrincipal user = null) {

		return new EnumeratorWrapper(new Enumerator.AllSubscriptionFiltered(
			bus: publisher,
			expiryStrategy: DefaultExpiryStrategy.Instance,
			checkpoint: checkpoint,
			resolveLinks: false,
			eventFilter: eventFilter,
			user: user ?? SystemAccounts.System,
			requiresLeader: false,
			maxSearchWindow: maxSearchWindow,
			checkpointIntervalMultiplier: checkpointIntervalMultiplier,
			cancellationToken: CancellationToken.None));
	}


	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class subscribe_filtered_all_from_start<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private readonly List<Guid> _eventIds = new();

		protected override void Given() {
			EnableReadAll();
			_eventIds.Add(WriteEvent("test-stream", "type1", "{}", "{Data: 1}").Item1.EventId);
			WriteEvent("test-stream", "type2", "{}", "{Data: 2}");
			WriteEvent("test-stream", "type3", "{}", "{Data: 3}");
		}

		[Test]
		public async Task should_receive_live_caught_up_message_after_reading_existing_events() {
			await using var sub = CreateAllSubscriptionFiltered(
				_publisher, null, EventFilter.EventType.Prefixes(false, "type1"));

			Assert.True(await sub.GetNext() is SubscriptionConfirmation);
			Assert.AreEqual(_eventIds[0], ((Event)await sub.GetNext()).Id);

			var checkpoint = AssertEx.IsType<Checkpoint>(await sub.GetNext());
			Assert.True(checkpoint.CheckpointPosition < Position.End);
			Assert.True(DateTime.UtcNow - checkpoint.Wrapped.Timestamp < TimeSpan.FromSeconds(1));

			var caughtUp = AssertEx.IsType<CaughtUp>(await sub.GetNext());
			Assert.True(DateTime.UtcNow - caughtUp.Wrapped.Timestamp < TimeSpan.FromSeconds(1));
			Assert.AreEqual(new TFPos(150, 100), caughtUp.Wrapped.AllCheckpoint);
			Assert.Null(caughtUp.Wrapped.StreamCheckpoint);
		}

		[Test]
		public async Task when_matching_nothing_transition_to_live_checkpoint_should_be_valid() {
			await using var sub = CreateAllSubscriptionFiltered(
				_publisher, null, EventFilter.EventType.Prefixes(false, "match-nothing"));

			Assert.True(await sub.GetNext() is SubscriptionConfirmation);

			var checkpoint = AssertEx.IsType<Checkpoint>(await sub.GetNext());
			Assert.True(checkpoint.CheckpointPosition < Position.End);
			Assert.True(DateTime.UtcNow - checkpoint.Wrapped.Timestamp < TimeSpan.FromSeconds(1));

			var caughtUp = AssertEx.IsType<CaughtUp>(await sub.GetNext());
			Assert.True(DateTime.UtcNow - caughtUp.Wrapped.Timestamp < TimeSpan.FromSeconds(1));
			Assert.AreEqual(new TFPos(0, 0), caughtUp.Wrapped.AllCheckpoint);
			Assert.Null(caughtUp.Wrapped.StreamCheckpoint);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class subscribe_filtered_all_from_end<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		protected override void Given() {
			EnableReadAll();
			WriteEvent("test-stream", "type1", "{}", "{Data: 1}");
			WriteEvent("test-stream", "type2", "{}", "{Data: 2}");
			WriteEvent("test-stream", "type3", "{}", "{Data: 3}");
		}

		[Test]
		public async Task should_receive_live_caught_up_message_immediately() {
			await using var sub = CreateAllSubscriptionFiltered(
				_publisher, Position.End, EventFilter.EventType.Prefixes(false, "type1"));

			Assert.True(await sub.GetNext() is SubscriptionConfirmation);
			var caughtUp = AssertEx.IsType<CaughtUp>(await sub.GetNext());
			Assert.True(DateTime.UtcNow - caughtUp.Wrapped.Timestamp < TimeSpan.FromSeconds(1));
			Assert.AreEqual(new TFPos(400, 400), caughtUp.Wrapped.AllCheckpoint);
			Assert.Null(caughtUp.Wrapped.StreamCheckpoint);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class subscribe_filtered_all_from_position<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private readonly List<Guid> _eventIds = new();
		private TFPos _subscribeFrom;

		protected override void Given() {
			EnableReadAll();
			WriteEvent("test-stream", "theType", "{}", "{Data: 1}");
			WriteEvent("test-stream", "type2", "{}", "{Data: 2}");
			(_, _subscribeFrom) = WriteEvent("test-stream", "theType", "{}", "{Data: 2}");
			_eventIds.Add(WriteEvent("test-stream", "theType", "{}", "{Data: 3}").Item1.EventId);
			WriteEvent("test-stream", "type3", "{}", "{Data: 3}");
			WriteEvent("test-stream", "type4", "{}", "{Data: 4}");
		}

		[Test]
		public async Task should_receive_matching_events_after_start_position() {
			await using var sub = CreateAllSubscriptionFiltered(
				_publisher,
				new Position((ulong)_subscribeFrom.CommitPosition, (ulong)_subscribeFrom.PreparePosition),
				EventFilter.EventType.Prefixes(false, "theType"));

			Assert.True(await sub.GetNext() is SubscriptionConfirmation);
			Assert.AreEqual(_eventIds[0], ((Event)await sub.GetNext()).Id);

			var checkpoint = AssertEx.IsType<Checkpoint>(await sub.GetNext());
			Assert.True(checkpoint.CheckpointPosition < Position.End);
			Assert.True(DateTime.UtcNow - checkpoint.Wrapped.Timestamp < TimeSpan.FromSeconds(1));

			var caughtUp = AssertEx.IsType<CaughtUp>(await sub.GetNext());
			Assert.True(DateTime.UtcNow - caughtUp.Wrapped.Timestamp < TimeSpan.FromSeconds(1));
			Assert.AreEqual(new TFPos(450, 400), caughtUp.Wrapped.AllCheckpoint);
			Assert.Null(caughtUp.Wrapped.StreamCheckpoint);
		}

		[Test]
		public async Task when_matching_nothing_transition_to_live_checkpoint_should_be_valid() {
			await using var sub = CreateAllSubscriptionFiltered(
				_publisher,
				new Position((ulong)_subscribeFrom.CommitPosition, (ulong)_subscribeFrom.PreparePosition),
				EventFilter.EventType.Prefixes(false, "match-nothing"));

			Assert.True(await sub.GetNext() is SubscriptionConfirmation);

			var checkpoint = AssertEx.IsType<Checkpoint>(await sub.GetNext());
			Assert.True(checkpoint.CheckpointPosition < Position.End);
			Assert.True(DateTime.UtcNow - checkpoint.Wrapped.Timestamp < TimeSpan.FromSeconds(1));

			var caughtUp = AssertEx.IsType<CaughtUp>(await sub.GetNext());
			Assert.True(DateTime.UtcNow - caughtUp.Wrapped.Timestamp < TimeSpan.FromSeconds(1));
			Assert.AreEqual(new TFPos(350, 300), caughtUp.Wrapped.AllCheckpoint);
			Assert.Null(caughtUp.Wrapped.StreamCheckpoint);
		}
	}
}
