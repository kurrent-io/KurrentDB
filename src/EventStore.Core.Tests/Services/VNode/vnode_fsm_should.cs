// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using DotNext.Runtime;
using EventStore.Core.Data;
using EventStore.Core.Messaging;
using EventStore.Core.Services.VNode;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.VNode;

internal abstract class P : Message {
}

internal class A : P {
}

internal class B : P {
}

internal class C : Message {
}

[TestFixture]
public class vnode_fsm_should {
	[Test]
	public async Task allow_ignoring_messages_by_common_ancestor() {
		var fsm = new VNodeFSMBuilder(new ValueReference<VNodeState>(VNodeState.Leader))
			.InAnyState()
			.When<P>().Ignore()
			.WhenOther().Do(x => Assert.Fail("{0} slipped through", x.GetType().Name))
			.Build();

		await fsm.HandleAsync(new A());
		await fsm.HandleAsync(new B());
	}

	[Test]
	public async Task handle_specific_message_even_if_base_message_is_ignored() {
		bool aHandled = false;
		var fsm = new VNodeFSMBuilder(new ValueReference<VNodeState>(VNodeState.Leader))
			.InAnyState()
			.When<P>().Ignore()
			.When<A>().Do(x => aHandled = true)
			.WhenOther().Do(x => Assert.Fail("{0} slipped through", x.GetType().Name))
			.Build();

		await fsm.HandleAsync(new A());
		await fsm.HandleAsync(new B());

		Assert.IsTrue(aHandled);
	}

	[Test]
	public async Task ignore_base_handler_if_derived_message_published() {
		var fsm = new VNodeFSMBuilder(new ValueReference<VNodeState>(VNodeState.Leader))
			.InAnyState()
			.When<P>()
			.Do(x => Assert.Fail("shouldn't call this"))

			.InState(VNodeState.Leader)
			.When<A>()
			.Ignore()
			.Build();

		await fsm.HandleAsync(new A());
	}

	[Test]
	public async Task ignore_base_handler_if_derived_message_published_diff_reg_order() {
		var fsm = new VNodeFSMBuilder(new ValueReference<VNodeState>(VNodeState.Leader))
			.InState(VNodeState.Leader)
			.When<A>()
			.Ignore()

			.InAnyState()
			.When<P>()
			.Do(x => Assert.Fail("shouldn't call this"))
			.Build();

		await fsm.HandleAsync(new A());
	}
}
