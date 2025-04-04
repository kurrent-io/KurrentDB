// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Other;

[TestFixture]
class Stopwatch {
	[Test]
	public void MeasureStopwatch() {
		var sw = new System.Diagnostics.Stopwatch();
		var measured = new System.Diagnostics.Stopwatch();
		sw.Reset();
		sw.Start();
		measured.Start();
		measured.Stop();
		TestHelper.Consume(measured.ElapsedMilliseconds);
		sw.Stop();
		TestHelper.Consume(sw.ElapsedMilliseconds);
		measured.Reset();
		sw.Reset();

		sw.Start();
		sw.Stop();
		var originalTime = sw.ElapsedMilliseconds;
		sw.Reset();

		sw.Start();
		for (var i = 0; i < 1000000; i++) {
			measured.Start();
			measured.Stop();
			TestHelper.Consume(measured.ElapsedMilliseconds);
		}

		sw.Stop();
		var measuredTime = sw.ElapsedMilliseconds;
		Console.WriteLine(measuredTime - originalTime);
	}
}
