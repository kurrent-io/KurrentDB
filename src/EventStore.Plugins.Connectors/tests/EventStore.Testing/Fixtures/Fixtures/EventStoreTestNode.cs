using System.Net;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Common;
using EventStore.Testing.FluentDocker;
using Polly;
using Polly.Contrib.WaitAndRetry;

namespace EventStore.Testing.Fixtures;

public class EventStoreTestNode(ConfigureTestContainer? configureFixtureOptions = null) : TestContainerService {
    public EventStoreTestContainerOptions Options { get; private set; } = null!;

    int NodePort => Options.ClientSettings.ConnectivitySettings.Address!.Port;
    
    protected override async Task<ContainerBuilder> ConfigureAsync() {
        Options = EventStoreTestContainerOptions.CreateDefaultSingleNodeOptions(await NetworkPortProvider.GetNextAvailablePort());
        
        if (configureFixtureOptions is not null)
            Options = configureFixtureOptions(Options);
        
        var env = Options.Environment.Select(pair => $"{pair.Key}={pair.Value}").ToArray();

        var serviceName = $"esdb-streaming-{NodePort}-{Guid.NewGuid().ToString()[30..]}";
        
        return new Builder()
            .UseContainer()
            .UseImage(Options.Environment["CI_EVENTSTORE_DOCKER_IMAGE"])
            .WithName(serviceName)
            .WithEnvironment(env)
            .ExposePort(NodePort, 2113);
    }

    static readonly IEnumerable<TimeSpan> DefaultBackoffDelay = Backoff.ConstantBackoff(TimeSpan.FromMilliseconds(500), 60);

    protected override async Task OnServiceStarted() {
        using var http = new HttpClient(new SocketsHttpHandler { SslOptions = { RemoteCertificateValidationCallback = delegate { return true; } } });

        http.BaseAddress = Options.ClientSettings.ConnectivitySettings.Address;

        await Policy.Handle<Exception>()
            .WaitAndRetryAsync(DefaultBackoffDelay)
            .ExecuteAsync(
                async () => {
                    using var response = await http.GetAsync("/health/live", CancellationToken.None);
                    if (response.StatusCode >= HttpStatusCode.BadRequest)
                        throw new FluentDockerException($"health check failed with status code: {response.StatusCode}.");
                }
            );
    }

    protected override async Task OnServiceStopped() {
        await NetworkPortProvider.ReleasePorts(NodePort);
    }
}