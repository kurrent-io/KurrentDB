// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Plugins;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.VNode;

[TestFixture]
public class subsystem_should : SpecificationWithDirectory {
	[Test]
	public async Task report_as_initialised_after_being_started_successfully() {
		var tcs = TaskCompletionSourceFactory.CreateDefault();

		await using var node = new MiniNode<LogFormat.V2, string>(PathName, subsystems: [new FakeSubSystem()]);
		node.Node.MainBus.Subscribe(new AdHocHandler<SystemMessage.SystemReady>(t => {
			tcs.TrySetResult();
		}));

		_ = node.Start();

		// SystemReady is received after all subsystems have started.
		await tcs.Task.WithTimeout(TimeSpan.FromSeconds(5));
	}

	class FakeSubSystem() : SubsystemsPlugin("FakeSubSystem");
}
