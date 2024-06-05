using System.Collections.Immutable;
using EventStore.Client;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Extensions.Logging;

namespace EventStore.Testing.Fixtures;

public delegate EventStoreTestContainerOptions ConfigureTestContainer(EventStoreTestContainerOptions options);

public record EventStoreTestContainerOptions(EventStoreClientSettings ClientSettings, IDictionary<string, string?> Environment) {
    public EventStoreTestContainerOptions RunInMemory(bool runInMemory = true) =>
        this with { Environment = Environment.With(x => x["EVENTSTORE_MEM_DB"] = runInMemory.ToString()) };

    public EventStoreTestContainerOptions RunProjections(bool runProjections = true) =>
        this with {
            Environment = Environment.With(
                x => {
                    x["EVENTSTORE_START_STANDARD_PROJECTIONS"] = runProjections.ToString();
                    x["EVENTSTORE_RUN_PROJECTIONS"]            = runProjections ? "All" : "None";
                }
            )
        };
    
    public static EventStoreTestContainerOptions CreateDefaultSingleNodeOptions(int nodePort) {
        var defaultSettings = EventStoreClientSettings
            .Create($"esdb://admin:changeit@localhost:{nodePort}/?tls=false&tlsVerifyCert=false")
            .With(x => x.ConnectionName = "esdb-streaming")
            .With(x => x.LoggerFactory = new SerilogLoggerFactory(Log.Logger))
            .With(x => x.DefaultDeadline = Application.DebuggerIsAttached ? TimeSpan.MaxValue : x.DefaultDeadline);

        var variables = new Dictionary<string, string?>(
            Application.Configuration
                .EnsureValue("CI_EVENTSTORE_DOCKER_IMAGE", "docker.eventstore.com/eventstore-ce/eventstoredb-ce:latest")
                .EnsureValue("EVENTSTORE_TELEMETRY_OPTOUT", "true")
                .EnsureValue("EVENTSTORE_INSECURE", "true")
                .EnsureValue("EVENTSTORE_MEM_DB", "false")
                .EnsureValue("EVENTSTORE_RUN_PROJECTIONS", "All")
                .EnsureValue("EVENTSTORE_START_STANDARD_PROJECTIONS", "true")
                .EnsureValue("EVENTSTORE_LOG_LEVEL", "Information")
                .EnsureValue("EVENTSTORE_DISABLE_LOG_FILE", "true")
                .EnsureValue("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP", "true")
                .AsEnumerable()
        );

        if (nodePort != NetworkPortProvider.DefaultEsdbPort) {
            if (variables.TryGetValue("CI_EVENTSTORE_DOCKER_IMAGE", out var imageTag) && imageTag == "ci")
                variables["EVENTSTORE_ADVERTISE_NODE_PORT_TO_CLIENT_AS"] = $"{nodePort}";
            else
                variables["EVENTSTORE_ADVERTISE_HTTP_PORT_TO_CLIENT_AS"] = $"{nodePort}";
        }

        var orderedVariables = variables.Where(x => x.Key.StartsWith("CI_EVENTSTORE_") || x.Key.StartsWith("EVENTSTORE_"))
            .OrderBy(x => x.Key)
            .ToImmutableDictionary(x => x.Key, x => x.Value);

        return new(defaultSettings, new Dictionary<string, string?>(orderedVariables));
    }

    public static EventStoreTestContainerOptions CreateDefaultClusterOptions(IReadOnlyList<int> nodePorts) {
        var defaultSettings = EventStoreClientSettings
            .Create($"esdb://{string.Join(',', nodePorts.Select(port => $"localhost:{port}"))}?tls=true&tlsVerifyCert=false")
            .With(x => x.LoggerFactory = new SerilogLoggerFactory(Log.Logger))
            .With(x => x.DefaultDeadline = Application.DebuggerIsAttached ? new TimeSpan?() : TimeSpan.FromSeconds(30))
            .With(x => x.ConnectivitySettings.MaxDiscoverAttempts = 30)
            .With(x => x.ConnectivitySettings.DiscoveryInterval = TimeSpan.FromSeconds(1));

        var defaultEnvironment = new Dictionary<string, string?> {
            ["ES_CERTS_CLUSTER"]                        = Path.Combine(System.Environment.CurrentDirectory, "certs-cluster"),
            ["EVENTSTORE_CLUSTER_SIZE"]                 = "3",
            ["EVENTSTORE_HTTP_PORT"]                    = "2113",
            ["EVENTSTORE_DISCOVER_VIA_DNS"]             = "false",
            ["EVENTSTORE_ENABLE_EXTERNAL_TCP"]          = "false",
            ["EVENTSTORE_STREAM_EXISTENCE_FILTER_SIZE"] = "10000",
            ["EVENTSTORE_STREAM_INFO_CACHE_CAPACITY"]   = "10000",
            ["EVENTSTORE_TELEMETRY_OPTOUT"]             = "true"
        };

        for (var i = 0; i < nodePorts.Count; i++)
            defaultEnvironment.Add(
                i == nodePorts.Count
                    ? "CI_EVENTSTORE_CLUSTER_ROR_PORT"
                    : $"CI_EVENTSTORE_CLUSTER_NODE{i + 1}_PORT",
                $"{nodePorts[i]}"
            );

        return new(defaultSettings, defaultEnvironment);
    }
}