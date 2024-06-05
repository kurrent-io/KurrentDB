using Ductus.FluentDocker.Builders;
using EventStore.Testing.FluentDocker;

namespace EventStore.Testing.Fixtures;

public class EventStoreTestCluster(ConfigureTestContainer? configureFixtureOptions = null) : TestCompositeService {
    // Last node will always be a read-only replica.
    const int ClusterSize = 4;
    
    EventStoreTestContainerOptions Options { get; set; } = null!;
    
    int[] NodePorts => Options.ClientSettings.ConnectivitySettings.IpGossipSeeds!.Select(x => x.Port).ToArray();

    protected override async Task<CompositeBuilder> ConfigureAsync() {
        Options = EventStoreTestContainerOptions.CreateDefaultClusterOptions(await NetworkPortProvider.GetNumberOfPorts(ClusterSize));
        
        if (configureFixtureOptions is not null)
            Options = configureFixtureOptions(Options);

        var env = Options.Environment.Select(pair => $"{pair.Key}={pair.Value}").ToArray();

        var serviceName = $"esdb-test-cluster-{string.Join('_', NodePorts)}-{Guid.NewGuid().ToString()[30..]}";
        
        var builder = new Builder()
            .UseContainer()
            .FromComposeFile("docker-compose.yml")
            .ServiceName(serviceName)
            .WithEnvironment(env)
            .RemoveOrphans()
            .NoRecreate();

        return builder;
    }

    protected override async Task OnServiceStarted() {
        await Service.WaitUntilNodesAreHealthy("esdb-node", TimeSpan.FromSeconds(60));
    }
    
    protected override async Task OnServiceStopped() {
        await NetworkPortProvider.ReleasePorts(NodePorts);
    }
}