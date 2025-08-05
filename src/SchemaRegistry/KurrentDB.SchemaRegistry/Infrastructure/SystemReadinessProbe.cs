// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SchemaRegistry.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;

namespace KurrentDB.SchemaRegistry.Infrastructure.System;

public interface ISystemReadinessProbe {
    ValueTask<NodeSystemInfo> WaitUntilReady(CancellationToken cancellationToken);
}

[UsedImplicitly]
public class SystemReadinessProbe : IHandle<SystemMessage.BecomeLeader>, IHandle<SystemMessage.BecomeFollower>, IHandle<SystemMessage.BecomeReadOnlyReplica> {
    public SystemReadinessProbe(ISubscriber subscriber, GetNodeSystemInfo getNodeSystemInfo) {
        CompletionSource = new();

        Subscriber = subscriber.With(x => {
            x.Subscribe<SystemMessage.BecomeLeader>(this);
            x.Subscribe<SystemMessage.BecomeFollower>(this);
            x.Subscribe<SystemMessage.BecomeReadOnlyReplica>(this);
        });

        GetNodeSystemInfo = getNodeSystemInfo;
    }

    ISubscriber          Subscriber        { get; }
    GetNodeSystemInfo    GetNodeSystemInfo { get; }
    TaskCompletionSource CompletionSource  { get; }

    public void Handle(SystemMessage.BecomeLeader message)          => CompletionSource.TrySetResult();
    public void Handle(SystemMessage.BecomeFollower message)        => CompletionSource.TrySetResult();
    public void Handle(SystemMessage.BecomeReadOnlyReplica message) => CompletionSource.TrySetResult();

    public async ValueTask<NodeSystemInfo> WaitUntilReady(CancellationToken cancellationToken = default) {
        await CompletionSource.Task.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
        Subscriber.Unsubscribe<SystemMessage.BecomeLeader>(this);
        Subscriber.Unsubscribe<SystemMessage.BecomeFollower>(this);
        Subscriber.Unsubscribe<SystemMessage.BecomeReadOnlyReplica>(this);
        return await GetNodeSystemInfo();
    }
}
