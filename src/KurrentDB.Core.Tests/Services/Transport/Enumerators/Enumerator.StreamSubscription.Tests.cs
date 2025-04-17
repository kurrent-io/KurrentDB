// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Transport.Enumerators;

[TestFixture]
public partial class EnumeratorTests {
	private static EnumeratorWrapper CreateStreamSubscription<TStreamId>(
		IPublisher publisher,
		string streamName,
		StreamRevision? checkpoint = null,
		ClaimsPrincipal user = null) {

		return new EnumeratorWrapper(new Enumerator.StreamSubscription<TStreamId>(
			bus: publisher,
			expiryStrategy: new DefaultExpiryStrategy(),
			streamName: streamName,
			checkpoint: checkpoint,
			resolveLinks: false,
			user: user ?? SystemAccounts.System,
			requiresLeader: false,
			cancellationToken: CancellationToken.None));
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class subscribe_stream_from_start_<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private readonly List<Guid> _eventIds = new();

		protected override void Given() {
			EnableReadAll();
			_eventIds.Add(WriteEvent("test-stream1", "type1", "{}", "{Data: 1}").Item1.EventId);
			WriteEvent("test-stream2", "type2", "{}", "{Data: 2}");
			WriteEvent("test-stream3", "type3", "{}", "{Data: 3}");
		}

		[Test]
		public async Task should_receive_live_caught_up_message_after_reading_existing_events() {
			await using var sub = CreateStreamSubscription<TStreamId>(
				_publisher, streamName: "test-stream1");

			Assert.True(await sub.GetNext() is SubscriptionConfirmation);
			Assert.AreEqual(_eventIds[0], ((Event)await sub.GetNext()).Id);
			var caughtUp = AssertEx.IsType<CaughtUp>(await sub.GetNext());
			Assert.True(DateTime.UtcNow - caughtUp.Wrapped.Timestamp < TimeSpan.FromSeconds(1));
			Assert.Null(caughtUp.Wrapped.AllCheckpoint);
			Assert.AreEqual(0, caughtUp.Wrapped.StreamCheckpoint);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class subscribe_empty_stream_from_start<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		[Test]
		public async Task caught_up_checkpoint_is_negative1_when_stream_is_empty() {
			await using var sub = CreateStreamSubscription<TStreamId>(
				_publisher, streamName: "no-stream");

			Assert.True(await sub.GetNext() is SubscriptionConfirmation);
			var caughtUp = AssertEx.IsType<CaughtUp>(await sub.GetNext());
			Assert.True(DateTime.UtcNow - caughtUp.Wrapped.Timestamp < TimeSpan.FromSeconds(1));
			Assert.Null(caughtUp.Wrapped.AllCheckpoint);
			Assert.AreEqual(-1, caughtUp.Wrapped.StreamCheckpoint);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class subscribe_stream_from_end<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private readonly List<Guid> _eventIds = new();

		protected override void Given() {
			EnableReadAll();
			_eventIds.Add(WriteEvent("test-stream1", "type1", "{}", "{Data: 1}").Item1.EventId);
			WriteEvent("test-stream2", "type2", "{}", "{Data: 2}");
			WriteEvent("test-stream3", "type3", "{}", "{Data: 3}");
		}

		[Test]
		public async Task should_receive_live_caught_up_message_immediately() {
			await using var enumerator = CreateStreamSubscription<TStreamId>(
				_publisher, streamName: "test-stream1", StreamRevision.End);

			Assert.True(await enumerator.GetNext() is SubscriptionConfirmation);
			var caughtUp = AssertEx.IsType<CaughtUp>(await enumerator.GetNext());
			Assert.True(DateTime.UtcNow - caughtUp.Wrapped.Timestamp < TimeSpan.FromSeconds(1));
			Assert.Null(caughtUp.Wrapped.AllCheckpoint);
			Assert.AreEqual(0, caughtUp.Wrapped.StreamCheckpoint);
		}
	}
}
