// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Integration;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_a_single_node_is_shutdown<TLogFormat, TStreamId> : SpecificationWithDirectory {
	[Test]
	public async Task throws_on_timeout() {
		var node = new MiniNode<TLogFormat, TStreamId>(PathName);
		try {
			await node.Start();

			var shutdownTask = node.Node.StopAsync(TimeSpan.FromMilliseconds(1));
			Assert.ThrowsAsync<TimeoutException>(() => shutdownTask);
		} finally {
			await node.Shutdown();
		}
	}
}
