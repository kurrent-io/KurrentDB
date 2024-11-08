using EventStore.Core.Bus;
using EventStore.Core.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.System;

public abstract class NodeBackgroundService : IHostedService, IDisposable {
    Task?                    _executeTask;
    CancellationTokenSource? _stoppingCts;

    protected NodeBackgroundService(IPublisher publisher, ILogger<NodeBackgroundService> logger, string? serviceName = null) {
        Publisher   = publisher;
        Logger      = logger;
        ServiceName = serviceName ?? GetType().Name;
    }

    IPublisher       Publisher   { get; }
    ILogger          Logger      { get; }

    public string ServiceName { get; }

    public Task? ExecuteTask => _executeTask;

    /// <summary>
    /// This method is called when the <see cref="IHostedService"/> starts. The implementation should return a task that represents
    /// the lifetime of the long-running operation(s) being performed.
    /// </summary>
    /// <param name="stoppingToken">Triggered when <see cref="IHostedService.StopAsync(CancellationToken)"/> is called.</param>
    /// <returns>A <see cref="Task"/> that represents the long-running operations.</returns>
    /// <remarks>See <see href="https://docs.microsoft.com/dotnet/core/extensions/workers">Worker Services in .NET</see> for implementation guidelines.</remarks>
    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

    /// <inheritdoc />
    public virtual Task StartAsync(CancellationToken cancellationToken) {
        Logger.LogNodeBackgroundServiceStarted(ServiceName);

        // Create linked token to allow cancelling executing task from provided token
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Store the task we're executing
        _executeTask = Task.Run(async () => {
                try {
                    Publisher.Publish(
                        new SystemMessage.RegisterForGracefulTermination(ServiceName, () => _ = StopAsync(CancellationToken.None))
                    );

                    Logger.LogNodeBackgroundServiceOperationExecuting(ServiceName);

                    await ExecuteAsync(_stoppingCts.Token);
                }
                catch (Exception ex) {
                    Logger.LogNodeBackgroundServiceOperationExecutionError(ex, ServiceName, ex.Message);
                    throw;
                }
                finally {
                    Publisher.Publish(new SystemMessage.ComponentTerminated(ServiceName));
                    Logger.LogNodeBackgroundServiceOperationExecuted(ServiceName);
                }
            },
            _stoppingCts.Token
        );

        // If the task is completed then return it, this will bubble cancellation and failure to the caller
        return _executeTask.IsCompleted
            ? _executeTask
            // Otherwise it's running
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual async Task StopAsync(CancellationToken cancellationToken) {
        // Stop called without starting
        if (_executeTask == null)
            return;

        Logger.LogNodeBackgroundServiceStopping(ServiceName);

        try {
            // Signal cancellation to the executing method
            await _stoppingCts!.CancelAsync();
        }
        finally {
            // Wait until the task completes or the stop token triggers
            var completion = new TaskCompletionSource();

            await using var registration = cancellationToken
                .Register(tcs => ((TaskCompletionSource)tcs!).SetCanceled(CancellationToken.None), completion);

            // Do not await the _executeTask because cancelling it will throw an OperationCanceledException which we are explicitly ignoring
            await Task.WhenAny(_executeTask, completion.Task).ConfigureAwait(false);

            Logger.LogNodeBackgroundServiceStopped(ServiceName);
        }
    }

    /// <inheritdoc />
    public virtual void Dispose() {
        _stoppingCts?.Cancel();
        Logger.LogNodeBackgroundServiceDisposed(ServiceName);
    }
}

static partial class NodeBackgroundServiceLogMessages {
    [LoggerMessage(LogLevel.Trace, "{ServiceName} hosted service started")]
    internal static partial void LogNodeBackgroundServiceStarted(this ILogger logger, string serviceName);

    [LoggerMessage(LogLevel.Trace, "{ServiceName} hosted service stopping...")]
    internal static partial void LogNodeBackgroundServiceStopping(this ILogger logger, string serviceName);

    [LoggerMessage(LogLevel.Trace, "{ServiceName} hosted service stopped")]
    internal static partial void LogNodeBackgroundServiceStopped(this ILogger logger, string serviceName);

    [LoggerMessage(LogLevel.Trace, "{ServiceName} hosted service disposed")]
    internal static partial void LogNodeBackgroundServiceDisposed(this ILogger logger, string serviceName);

    [LoggerMessage(LogLevel.Trace, "{ServiceName} hosted service executing operation...")]
    internal static partial void LogNodeBackgroundServiceOperationExecuting(this ILogger logger, string serviceName);

    [LoggerMessage(LogLevel.Trace, "{ServiceName} hosted service operation executed")]
    internal static partial void LogNodeBackgroundServiceOperationExecuted(this ILogger logger, string serviceName);

    [LoggerMessage(LogLevel.Error, "{ServiceName} hosted service error detected: {ErrorMessage}")]
    internal static partial void LogNodeBackgroundServiceOperationExecutionError(this ILogger logger, Exception error, string serviceName, string errorMessage);
}