using DotNext.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Timeout = System.Threading.Timeout;

namespace EventStore.Connectors.Control;

public sealed class LeadershipAwareBackgroundService : BackgroundService {
    public LeadershipAwareBackgroundService(
        INodeLifetimeService nodeLifetime,
        Func<CancellationToken, Task> action,
        ILogger<LeadershipAwareBackgroundService>? logger = null
    ) {
        NodeLifetime = nodeLifetime;
        Action       = action;
        Logger       = logger ?? NullLoggerFactory.Instance.CreateLogger<LeadershipAwareBackgroundService>();
    }

    INodeLifetimeService          NodeLifetime { get; }
    Func<CancellationToken, Task> Action       { get; }
    ILogger                       Logger       { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        Logger.LogServiceStarted();

        while (!stoppingToken.IsCancellationRequested) {
            var leadershipToken = await NodeLifetime.WaitForLeadershipAsync(Timeout.InfiniteTimeSpan, stoppingToken);
            var token           = leadershipToken;

            var cancellator = token.LinkTo(stoppingToken);

            try {
                // it only runs on a leader node, so if the cancellation
                // token is cancelled, it means the node lost leadership
                await Action(cancellator!.Token);
                // lost leadership...
            }
            catch (OperationCanceledException) when (cancellator?.CancellationOrigin == stoppingToken) {
                Logger.LogServiceStopped();
                break;
            }
            finally {
                cancellator?.Dispose();
            }
        }
    }
}

static partial class LeadershipAwareBackgroundServiceLogMessages {
    [LoggerMessage(LogLevel.Debug, "Service started")]
    internal static partial void LogServiceStarted(this ILogger logger);

    [LoggerMessage(LogLevel.Debug, "Service stopped")]
    internal static partial void LogServiceStopped(this ILogger logger);
}