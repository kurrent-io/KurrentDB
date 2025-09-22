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
	private static EnumeratorWrapper CreateAllSubscription(
		IPublisher publisher,
		Position? checkpoint,
		ClaimsPrincipal user = null) {

		return new EnumeratorWrapper(new Enumerator.AllSubscription(
			bus: publisher,
			expiryStrategy: DefaultExpiryStrategy.Instance,
			checkpoint: checkpoint,
			resolveLinks: false,
			user: user ?? SystemAccounts.System,
			requiresLeader: false,
			cancellationToken: CancellationToken.None));
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class subscribe_all_from_start<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private readonly List<Guid> _eventIds = new();

		protected override void Given() {
			EnableReadAll();
			_eventIds.Add(WriteEvent("test-stream", "type1", "{}", "{Data: 1}").Item1.EventId);
			_eventIds.Add(WriteEvent("test-stream", "type2", "{}", "{Data: 2}").Item1.EventId);
			_eventIds.Add(WriteEvent("test-stream", "type3", "{}", "{Data: 3}").Item1.EventId);
		}

		[Test]
		public async Task should_receive_live_caught_up_message_after_reading_existing_events() {
			await using var sub = CreateAllSubscription(_publisher, checkpoint: null);

			Assert.True(await sub.GetNext() is SubscriptionConfirmation);
			Assert.AreEqual(_eventIds[0], ((Event)await sub.GetNext()).Id);
			Assert.AreEqual(_eventIds[1], ((Event)await sub.GetNext()).Id);
			Assert.AreEqual(_eventIds[2], ((Event)await sub.GetNext()).Id);
			var caughtUp = AssertEx.IsType<CaughtUp>(await sub.GetNext());
			Assert.True(DateTime.UtcNow - caughtUp.Wrapped.Timestamp < TimeSpan.FromSeconds(1));
			Assert.AreEqual(new TFPos(350, 300), caughtUp.Wrapped.AllCheckpoint);
			Assert.Null(caughtUp.Wrapped.StreamCheckpoint);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class subscribe_all_from_end<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		protected override void Given() {
			EnableReadAll();
			WriteEvent("test-stream", "type1", "{}", "{Data: 1}");
			WriteEvent("test-stream", "type2", "{}", "{Data: 2}");
			WriteEvent("test-stream", "type3", "{}", "{Data: 3}");
		}

		[Test]
		public async Task should_receive_live_caught_up_message_immediately() {
			await using var sub = CreateAllSubscription(_publisher, Position.End);

			Assert.True(await sub.GetNext() is SubscriptionConfirmation);
			var caughtUp = AssertEx.IsType<CaughtUp>(await sub.GetNext());
			Assert.True(DateTime.UtcNow - caughtUp.Wrapped.Timestamp < TimeSpan.FromSeconds(1));
			Assert.AreEqual(new TFPos(350, 350), caughtUp.Wrapped.AllCheckpoint);
			Assert.Null(caughtUp.Wrapped.StreamCheckpoint);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class subscribe_all_from_position<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private readonly List<Guid> _eventIds = new();
		private TFPos _subscribeFrom;

		protected override void Given() {
			EnableReadAll();
			WriteEvent("test-stream", "type1", "{}", "{Data: 1}");
			WriteEvent("test-stream", "type2", "{}", "{Data: 2}");
			(_, _subscribeFrom) = WriteEvent("test-stream", "type3", "{}", "{Data: 3}");
			_eventIds.Add(WriteEvent("test-stream", "type4", "{}", "{Data: 4}").Item1.EventId);
			_eventIds.Add(WriteEvent("test-stream", "type5", "{}", "{Data: 5}").Item1.EventId);
			_eventIds.Add(WriteEvent("test-stream", "type6", "{}", "{Data: 6}").Item1.EventId);
		}

		[Test]
		public async Task should_receive_events_after_start_position() {
			await using var sub = CreateAllSubscription(
				_publisher,
				new Position((ulong)_subscribeFrom.CommitPosition, (ulong)_subscribeFrom.PreparePosition));

			Assert.True(await sub.GetNext() is SubscriptionConfirmation);
			Assert.AreEqual(_eventIds[0], ((Event)await sub.GetNext()).Id);
			Assert.AreEqual(_eventIds[1], ((Event)await sub.GetNext()).Id);
			Assert.AreEqual(_eventIds[2], ((Event)await sub.GetNext()).Id);
			var caughtUp = AssertEx.IsType<CaughtUp>(await sub.GetNext());
			Assert.True(DateTime.UtcNow - caughtUp.Wrapped.Timestamp < TimeSpan.FromSeconds(1));
			Assert.AreEqual(new TFPos(650, 600), caughtUp.Wrapped.AllCheckpoint);
			Assert.Null(caughtUp.Wrapped.StreamCheckpoint);
		}
	}
}
