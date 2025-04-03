// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Helpers;
using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.VNode;

[TestFixture]
public class subsystem_should : SpecificationWithDirectory {
	[Test]
	public async Task report_as_initialised_after_being_started_successfully() {
		var tcs = new TaskCompletionSource();

		await using var node = new MiniNode<LogFormat.V2,string>(PathName, subsystems: [ new FakeSubSystem() ]);
		node.Node.MainBus.Subscribe(new AdHocHandler<SystemMessage.SystemReady>( t => {
			tcs.TrySetResult();
		}));

		_ = node.Start();

		// SystemReady is received after all subsystems have started.
		await tcs.Task.WithTimeout(TimeSpan.FromSeconds(5));
	}

	class FakeSubSystem() : SubsystemsPlugin("FakeSubSystem");
}
