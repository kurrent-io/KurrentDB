// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace KurrentDB.Core.Hosting;

/// <summary>
/// A one-shot completion source that hands the waiter a cancellation token which the
/// producer can revoke afterwards.
/// </summary>
public sealed class TokenCompletionSource : IDisposable {
    readonly CancellationTokenSource                 _cancellator = new();
    readonly TaskCompletionSource<CancellationToken> _completion  = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // The waiter is handed this source's OWN token. That is the whole point:
    // a later Cancel() cancels the very token the caller is already holding, so
    // work that is already running finds out it must stop.
    public Task<CancellationToken> Task => _completion.Task;

    public void Complete() => _completion.TrySetResult(_cancellator.Token);

    public void Cancel() {
        _cancellator.Cancel();

        // Release a waiter that parked before the signal ever arrived — otherwise
        // disposing the owner would leave it awaiting forever. The token it gets
        // back is already cancelled, which is exactly the "stop" answer it needs.
        _completion.TrySetResult(_cancellator.Token);
    }

    public void Dispose() => _cancellator.Dispose();
}
