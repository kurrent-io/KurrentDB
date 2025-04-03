// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Diagnostics;
using DotNext.Runtime;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.VNode;
using NUnit.Framework;

namespace EventStore.Core.Tests.Bus;

[TestFixture]
public class FSMSpeedTest {
	[Test, Category("LongRunning"), Explicit]
	public async Task FSMSpeedTest1() {
		var fsm = CreateFSM();
		var msg = new StorageMessage.WriteCommit(Guid.NewGuid(), new NoopEnvelope(), 0);
		const int iterations = 1000000;

		var ts = new Timestamp();

		for (int i = 0; i < iterations; ++i) {
			await fsm.HandleAsync(msg);
		}

		var elapsedMs = ts.ElapsedMilliseconds;
		Console.WriteLine($"Elapsed: {elapsedMs} ({elapsedMs / iterations} per item).");
	}

	[Test, Category("LongRunning"), Explicit]
	public async Task FSMSpeedTest2() {
		var bus = InMemoryBus.CreateTest();
		bus.Subscribe(new AdHocHandler<StorageMessage.WriteCommit>(x => { }));
		bus.Subscribe(new AdHocHandler<Message>(x => { }));

		var msg = new StorageMessage.WriteCommit(Guid.NewGuid(), new NoopEnvelope(), 0);
		const int iterations = 1000000;

		var ts = new Timestamp();

		for (int i = 0; i < iterations; ++i) {
			await bus.DispatchAsync(msg);
		}

		var elapsedMs = ts.ElapsedMilliseconds;
		Console.WriteLine($"Elapsed: {elapsedMs} ({elapsedMs / iterations} per item).");
	}

	private VNodeFSM CreateFSM() {
		var outputBus = InMemoryBus.CreateTest(false);
		var scheduler = new QueuedHandlerThreadPool(outputBus, "Test", new(), new());

		var stm = new VNodeFSMBuilder(new ValueReference<VNodeState>(VNodeState.Leader))
			.InAnyState()
			.When<SystemMessage.StateChangeMessage>().Do(m => { })
			.InState(VNodeState.Initializing)
			.When<SystemMessage.SystemInit>().Do(msg => { })
			.When<SystemMessage.SystemStart>().Do(msg => { })
			.When<SystemMessage.BecomePreLeader>().Do(msg => { })
			.When<SystemMessage.ServiceInitialized>().Do(msg => { })
			.WhenOther().ForwardTo(scheduler)
			.InStates(VNodeState.Initializing, VNodeState.ShuttingDown, VNodeState.Shutdown)
			.When<ClientMessage.ReadRequestMessage>().Do(msg => { })
			.InAllStatesExcept(VNodeState.Initializing, VNodeState.ShuttingDown, VNodeState.Shutdown)
			.When<ClientMessage.ReadRequestMessage>().ForwardTo(scheduler)
			.InAllStatesExcept(VNodeState.PreLeader)
			.When<SystemMessage.WaitForChaserToCatchUp>().Ignore()
			.When<SystemMessage.ChaserCaughtUp>().Ignore()
			.InState(VNodeState.PreLeader)
			.When<SystemMessage.BecomeLeader>().Do(msg => { })
			.When<SystemMessage.WaitForChaserToCatchUp>().ForwardTo(scheduler)
			.When<SystemMessage.ChaserCaughtUp>().Do(msg => { })
			.WhenOther().ForwardTo(scheduler)
			.InState(VNodeState.Leader)
			.When<ClientMessage.WriteEvents>().Do(msg => { })
			.When<ClientMessage.TransactionStart>().Do(msg => { })
			.When<ClientMessage.TransactionWrite>().Do(msg => { })
			.When<ClientMessage.TransactionCommit>().Do(msg => { })
			.When<ClientMessage.DeleteStream>().Do(msg => { })
			.When<StorageMessage.WritePrepares>().ForwardTo(scheduler)
			.When<StorageMessage.WriteDelete>().ForwardTo(scheduler)
			.When<StorageMessage.WriteTransactionStart>().ForwardTo(scheduler)
			.When<StorageMessage.WriteTransactionData>().ForwardTo(scheduler)
			.When<StorageMessage.WriteTransactionEnd>().ForwardTo(scheduler)
			.When<StorageMessage.WriteCommit>().ForwardTo(scheduler)
			.WhenOther().ForwardTo(scheduler)
			.InAllStatesExcept(VNodeState.Leader)
			.When<ClientMessage.WriteRequestMessage>().Do(msg => { })
			.When<StorageMessage.WritePrepares>().Ignore()
			.When<StorageMessage.WriteDelete>().Ignore()
			.When<StorageMessage.WriteTransactionStart>().Ignore()
			.When<StorageMessage.WriteTransactionData>().Ignore()
			.When<StorageMessage.WriteTransactionEnd>().Ignore()
			.When<StorageMessage.WriteCommit>().Ignore()
			.InAllStatesExcept(VNodeState.ShuttingDown, VNodeState.Shutdown)
			.When<ClientMessage.RequestShutdown>().Do(msg => { })
			.When<SystemMessage.BecomeShuttingDown>().Do(msg => { })
			.InState(VNodeState.ShuttingDown)
			.When<SystemMessage.BecomeShutdown>().Do(msg => { })
			.When<SystemMessage.ShutdownTimeout>().Do(msg => { })
			.InStates(VNodeState.ShuttingDown, VNodeState.Shutdown)
			.When<SystemMessage.ServiceShutdown>().Do(msg => { })
			.WhenOther().ForwardTo(scheduler)
			.Build();
		return stm;
	}
}
