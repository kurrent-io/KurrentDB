// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Scavenging;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_scavenge_cancelled_after_chunck_scavenged<TLogFormat, TStreamId> : ScavengeLifeCycleScenario<TLogFormat, TStreamId> {
	protected override async Task When() {
		var cancellationTokenSource = new CancellationTokenSource();

		Log.ChunkScavenged += (sender, args) => cancellationTokenSource.Cancel();
		await TfChunkScavenger.Scavenge(true, true, 0, ct: cancellationTokenSource.Token);
	}

	[Test]
	public void completed_logged_with_stopped_result() {
		Assert.That(Log.Completed);
		Assert.That(Log.Result, Is.EqualTo(ScavengeResult.Stopped));
	}

	[Test]
	public void scavenge_record_for_first_and_cancelled_chunk() {
		Assert.That(Log.Scavenged, Has.Count.EqualTo(1));
		Assert.That(Log.Scavenged[0].Scavenged, Is.True);
	}


	[Test]
	public void doesnt_call_scavenge_on_the_table_index() {
		Assert.That(FakeTableIndex.ScavengeCount, Is.EqualTo(0));
	}
}
