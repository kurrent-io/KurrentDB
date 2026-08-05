// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Frozen;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Core.Hosting.Experimental;

[UsedImplicitly]
public sealed class AdvancedSystemReadinessProbe : ISystemReadinessProbe, IHandle<SystemMessage.StateChangeMessage> {
    public AdvancedSystemReadinessProbe(ISubscriber subscriber, GetNodeSystemInfo getNodeInfo, NodeReadyWhen readyWhen) {
		CompletionSource = new();
        
        ReadyStates = readyWhen switch {
            NodeReadyWhen.Leader      => new[] { VNodeState.Leader }.ToFrozenSet(),
            NodeReadyWhen.Follower    => new[] { VNodeState.Follower }.ToFrozenSet(),
            NodeReadyWhen.Replica     => new[] { VNodeState.ReadOnlyReplica }.ToFrozenSet(),
            NodeReadyWhen.Candidate   => new[] { VNodeState.Leader, VNodeState.Follower }.ToFrozenSet(),
            NodeReadyWhen.Operational => new[] { VNodeState.Leader, VNodeState.Follower, VNodeState.ReadOnlyReplica }.ToFrozenSet(),
            _                     =>  throw new ArgumentOutOfRangeException(nameof(readyWhen), readyWhen, null)
        };

        Subscriber  = subscriber;
        GetNodeInfo = getNodeInfo;

        Subscriber.Subscribe(this);
    }

    ISubscriber           Subscriber       { get; }
    GetNodeSystemInfo     GetNodeInfo      { get; }
    FrozenSet<VNodeState> ReadyStates      { get; }
    TaskCompletionSource  CompletionSource { get; }

	// Bus thread — allocation-free, branch-only; StateChangeMessage fires often.
	// Latch: the first ready state opens the gate for good.
	public void Handle(SystemMessage.StateChangeMessage message) {
		if (ReadyStates.Contains(message.State))
			CompletionSource.TrySetResult();
	}

	public async ValueTask<NodeSystemInfo> WaitUntilReady(CancellationToken cancellationToken = default) {
        // A probe created late after the node already reached a ready state would wait forever
        // The gossip stream holds the current truth.
        try {
            var info = await GetNodeInfo(cancellationToken);
            if (ReadyStates.Contains(info.MemberInfo.State) && info.MemberInfo.IsAlive)
                CompletionSource.TrySetResult();
        }
        catch {
            // Node too early to serve the gossip read
        }
        
        await CompletionSource.Task.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
		Subscriber.Unsubscribe(this);
		return await GetNodeInfo(CancellationToken.None);
	}
}

public enum NodeReadyWhen {
    Operational = 0,
    Candidate   = 1,
    Replica     = 2,
    Follower    = 3,
    Leader      = 4
}


[UsedImplicitly]
public sealed class SystemReadiness(ISubscriber subscriber, GetNodeSystemInfo getNodeInfo) {
    public SystemReadiness(IServiceProvider services) : this(
        services.GetRequiredService<ISubscriber>(),
        services.GetRequiredService<GetNodeSystemInfo>()) { }

    public ISystemReadinessProbe CreateProbe(NodeReadyWhen readyWhen = NodeReadyWhen.Operational) => 
        new AdvancedSystemReadinessProbe(subscriber, getNodeInfo, readyWhen);
}