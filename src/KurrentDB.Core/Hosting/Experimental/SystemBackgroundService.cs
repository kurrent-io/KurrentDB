// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using Microsoft.Extensions.Hosting;
using static KurrentDB.Core.Messages.SystemMessage;

namespace KurrentDB.Core.Hosting.Experimental;

[UsedImplicitly]
public abstract class SystemBackgroundService(IPublisher publisher) : BackgroundService {
    protected abstract string ServiceName { get; }
    
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // Register BEFORE the gate wait. A service that is still waiting to start is then already known
        // to ShutdownService, so it is asked to stop rather than torn down underneath while it waits.
        publisher.Publish(new RegisterForGracefulTermination(ServiceName, () => _ = StopAsync(CancellationToken.None)));

        try {
            await RunAsync(stoppingToken).ConfigureAwait(false);
        }
        finally {
            // Deregister however OnExecuteAsync ended — normal stop, role loss, or fault — so core teardown
            // stops waiting on us.
            publisher.Publish(new ComponentTerminated(ServiceName));
        }
    }
    
    /// <summary>
    /// The body of the service, run once the subclass's startup gate opens. The token is cancelled when
    /// the host stops the service.
    /// </summary>
    protected abstract Task RunAsync(CancellationToken stoppingToken);
}
