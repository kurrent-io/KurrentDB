// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Enumerators;

[TestFixture]
public partial class EnumeratorTests {
	private static EnumeratorWrapper ReadAllForwards(IPublisher publisher, Position position) {
		return new EnumeratorWrapper(new Enumerator.ReadAllForwards(
			bus: publisher,
			position: position,
			maxCount: 10,
			resolveLinks: false,
			user: SystemAccounts.System,
			requiresLeader: false,
			deadline: DateTime.Now,
			cancellationToken: CancellationToken.None));
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class read_all_forwards<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private readonly List<Guid> _eventIds = new();

		protected override void Given() {
			EnableReadAll();
			_eventIds.Add(WriteEvent("test-stream", "type1", "{}", "{Data: 1}").Item1.EventId);
			_eventIds.Add(WriteEvent("test-stream", "type2", "{}", "{Data: 2}").Item1.EventId);
			_eventIds.Add(WriteEvent("test-stream", "type3", "{}", "{Data: 3}").Item1.EventId);
			_eventIds.Add(WriteEvent("test-stream-all", "type3", "{}", "{Data: 3}").Item1.EventId);
		}

		[Test]
		public async Task should_read_all_the_events() {
			await using var enumerator = ReadAllForwards(_publisher, Position.Start);

			Assert.AreEqual(_eventIds[0], ((Event)await enumerator.GetNext()).Id);
			Assert.AreEqual(_eventIds[1], ((Event)await enumerator.GetNext()).Id);
			Assert.AreEqual(_eventIds[2], ((Event)await enumerator.GetNext()).Id);
			Assert.AreEqual(_eventIds[3], ((Event)await enumerator.GetNext()).Id);
		}
	}
}
