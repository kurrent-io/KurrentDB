// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messages;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.Tests.Integration;

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_a_single_node_is_restarted_multiple_times<TLogFormat, TStreamId> : specification_with_a_single_node<TLogFormat, TStreamId> {
	private List<Guid> _epochIds = new List<Guid>();
	private const int _numberOfNodeStarts = 5;
	private readonly AutoResetEvent _waitForStart = new AutoResetEvent(false);

	protected override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(1);

	protected override void BeforeNodeStarts() {
		_node.Node.MainBus.Subscribe(new AdHocHandler<SystemMessage.EpochWritten>(Handle));
		base.BeforeNodeStarts();
	}

	protected override async Task Given() {
		for (int i = 0; i < _numberOfNodeStarts - 1; i++) {
			_waitForStart.WaitOne(5000);
			await ShutdownNode();
			await StartNode();
		}

		_waitForStart.WaitOne(5000);
		await base.Given();
	}

	private void Handle(SystemMessage.EpochWritten msg) {
		_epochIds.Add(msg.Epoch.EpochId);
		_waitForStart.Set();
	}

	[Test]
	public void should_be_a_different_epoch_for_every_startup() {
		Assert.AreEqual(_numberOfNodeStarts, _epochIds.Distinct().Count());
	}
}
