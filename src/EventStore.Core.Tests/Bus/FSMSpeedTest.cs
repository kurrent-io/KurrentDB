using System;
using System.Diagnostics;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.VNode;
using NUnit.Framework;

namespace EventStore.Core.Tests.Bus {
	[TestFixture]
	public class FSMSpeedTest {
		[Test, Category("LongRunning"), Explicit]
		public void FSMSpeedTest1() {
			var fsm = CreateFSM();
			var msg = new StorageMessage.WriteCommit(Guid.NewGuid(), new NoopEnvelope(), 0);
			const int iterations = 1000000;

			var sw = Stopwatch.StartNew();

			for (int i = 0; i < iterations; ++i) {
				fsm.Handle(msg);
			}

			sw.Stop();

			Console.WriteLine("Elapsed: {0} ({1} per item).", sw.Elapsed, sw.ElapsedMilliseconds / (float)iterations);
		}

		[Test, Category("LongRunning"), Explicit]
		public void FSMSpeedTest2() {
			var bus = new InMemoryBus("a", true);
			bus.Subscribe(new AdHocHandler<StorageMessage.WriteCommit>(x => { }));
			bus.Subscribe(new AdHocHandler<Message>(x => { }));

			var msg = new StorageMessage.WriteCommit(Guid.NewGuid(), new NoopEnvelope(), 0);
			const int iterations = 1000000;

			var sw = Stopwatch.StartNew();

			for (int i = 0; i < iterations; ++i) {
				bus.Handle(msg);
			}

			sw.Stop();

			Console.WriteLine("Elapsed: {0} ({1} per item).", sw.Elapsed, sw.ElapsedMilliseconds / (float)iterations);
		}

		private VNodeFSM CreateFSM() {
			var outputBus = new InMemoryBus("a", false);
			var state = VNodeState.Leader;
			var stm = new VNodeFSMBuilder(() => state)
				.InAnyState()
				.When<SystemMessage.StateChangeMessage>().Do(m => { })
				.InState(VNodeState.Initializing)
				.When<SystemMessage.SystemInit>().Do(msg => { })
				.When<SystemMessage.SystemStart>().Do(msg => { })
				.When<SystemMessage.BecomePreLeader>().Do(msg => { })
				.When<SystemMessage.ServiceInitialized>().Do(msg => { })
				.WhenOther().ForwardTo(outputBus)
				.InStates(VNodeState.Initializing, VNodeState.ShuttingDown, VNodeState.Shutdown)
				.When<ClientMessage.ReadRequestMessage>().Do(msg => { })
				.InAllStatesExcept(VNodeState.Initializing, VNodeState.ShuttingDown, VNodeState.Shutdown)
				.When<ClientMessage.ReadRequestMessage>().ForwardTo(outputBus)
				.InAllStatesExcept(VNodeState.PreLeader)
				.When<SystemMessage.WaitForChaserToCatchUp>().Ignore()
				.When<SystemMessage.ChaserCaughtUp>().Ignore()
				.InState(VNodeState.PreLeader)
				.When<SystemMessage.BecomeLeader>().Do(msg => { })
				.When<SystemMessage.WaitForChaserToCatchUp>().ForwardTo(outputBus)
				.When<SystemMessage.ChaserCaughtUp>().Do(msg => { })
				.WhenOther().ForwardTo(outputBus)
				.InState(VNodeState.Leader)
				.When<ClientMessage.WriteEvents>().Do(msg => { })
				.When<ClientMessage.TransactionStart>().Do(msg => { })
				.When<ClientMessage.TransactionWrite>().Do(msg => { })
				.When<ClientMessage.TransactionCommit>().Do(msg => { })
				.When<ClientMessage.DeleteStream>().Do(msg => { })
				.When<StorageMessage.WritePrepares>().ForwardTo(outputBus)
				.When<StorageMessage.WriteDelete>().ForwardTo(outputBus)
				.When<StorageMessage.WriteTransactionStart>().ForwardTo(outputBus)
				.When<StorageMessage.WriteTransactionData>().ForwardTo(outputBus)
				.When<StorageMessage.WriteTransactionEnd>().ForwardTo(outputBus)
				.When<StorageMessage.WriteCommit>().ForwardTo(outputBus)
				.WhenOther().ForwardTo(outputBus)
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
				.WhenOther().ForwardTo(outputBus)
				.Build();
			return stm;
		}
	}
}
