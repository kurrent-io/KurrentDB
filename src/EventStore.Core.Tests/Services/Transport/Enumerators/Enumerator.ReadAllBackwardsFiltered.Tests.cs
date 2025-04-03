// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Enumerators;

[TestFixture]
public partial class EnumeratorTests {
	private static EnumeratorWrapper ReadAllBackwardsFiltered(IPublisher publisher, Position position, IEventFilter filter = null) {
		return new EnumeratorWrapper(new Enumerator.ReadAllBackwardsFiltered(
			bus: publisher,
			position: position,
			maxCount: 10,
			resolveLinks: false,
			eventFilter: filter,
			user: SystemAccounts.System,
			requiresLeader: false,
			maxSearchWindow: null,
			deadline: DateTime.Now,
			cancellationToken: CancellationToken.None));
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class read_all_backwards_filtered<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private readonly List<Guid> _eventIds = new();

		protected override void Given() {
			EnableReadAll();
			_eventIds.Add(WriteEvent("test-stream", "type1", "{}", "{Data: 1}").Item1.EventId);
			_eventIds.Add(WriteEvent("test-stream", "type2", "{}", "{Data: 2}").Item1.EventId);
			_eventIds.Add(WriteEvent("test-stream", "type3", "{}", "{Data: 3}").Item1.EventId);
			_eventIds.Add(WriteEvent("test-stream-all", "type3", "{}", "{Data: 3}").Item1.EventId);
			_eventIds.Add(WriteEvent("test-stream-all", "type2", "{}", "{Data: 4}").Item1.EventId);
		}

		[Test]
		public async Task should_read_all_the_filtered_events() {
			await using var enumerator = ReadAllBackwardsFiltered(_publisher, Position.End,
				EventFilter.EventType.Prefixes(false, "type2"));

			Assert.AreEqual(_eventIds[4], ((Event)await enumerator.GetNext()).Id);
			Assert.AreEqual(_eventIds[1], ((Event)await enumerator.GetNext()).Id);

		}
	}
}
