// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Services.PersistentSubscription;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.PersistentSubscription;

[TestFixture]
public class RequestStatisticsTests {
	[Test]
	public void DoesNotOverflow() {
		var elapsedTicks = 0L;
		var sut = new RequestStatistics(() => elapsedTicks, 1000);

		for (var i = 0; i < 1000; i++) {
			var id = Guid.NewGuid();
			sut.StartOperation(id);

			elapsedTicks += TimeSpan.FromMinutes(36).Ticks;

			sut.EndOperation(id);
		}

		sut.GetMeasurementDetails();
	}
}
