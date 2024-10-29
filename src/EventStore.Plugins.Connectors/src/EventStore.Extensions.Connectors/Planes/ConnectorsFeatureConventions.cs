using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Schema;
using Humanizer;
using static EventStore.Streaming.Consumers.ConsumeFilter;

namespace EventStore.Connectors;

[PublicAPI]
public partial class ConnectorsFeatureConventions {
    [PublicAPI]
    [SuppressMessage("Usage", "CA2211:Non-constant fields should not be visible")]
    public static class Streams {
        public const string StreamPrefix           = "$connectors";
        public const string ManagementStreamPrefix = "$connectors-mngt";
        public const string ControlStreamPrefix    = "$connectors-ctrl";

        public static StreamTemplate ManagementStreamTemplate  = new ManagementStreamTemplate();  // $connectors/{0}
        public static StreamTemplate LeasesStreamTemplate      = new LeasesStreamTemplate();      // $connectors/{0}/leases
        public static StreamTemplate CheckpointsStreamTemplate = new CheckpointsStreamTemplate(); // $connectors/{0}/checkpoints
        public static StreamTemplate LifecycleStreamTemplate   = new LifecycleStreamTemplate();   // $connectors/{0}/lifecycle

        public static StreamId GetManagementStream(string connectorId)  => ManagementStreamTemplate.GetStream(connectorId);  // $connectors/3f9728
        public static StreamId GetLeasesStream(string connectorId)      => LeasesStreamTemplate.GetStream(connectorId);      // $connectors/3f9728/leases
        public static StreamId GetCheckpointsStream(string connectorId) => CheckpointsStreamTemplate.GetStream(connectorId); // $connectors/3f9728/checkpoints
        public static StreamId GetLifecycleStream(string connectorId)   => LifecycleStreamTemplate.GetStream(connectorId);   // $connectors/3f9728/lifecycle

        public static StreamId ControlConnectorsRegistryStream = $"${ControlStreamPrefix}/registry-snapshots";

        public static StreamId ManagementLifecycleReactorCheckpointsStream = $"{ManagementStreamPrefix}/lifecycle-rx/checkpoints";
        public static StreamId ManagementStreamSupervisorCheckpointsStream = $"{ManagementStreamPrefix}/supervisor-rx/checkpoints";
    }

    [PublicAPI]
    public static class Messages {
        public static string GetSystemMessageSubject(string category, string name) => $"$conn-{category}-{name.Kebaberize()}";

        public static string GetManagementMessageSubject(string name)    => GetSystemMessageSubject("mngt", name); // $conn-mngt-connector-created
        public static string GetControlSystemMessageSubject(string name) => GetSystemMessageSubject("ctrl", name); // $conn-ctrl-message-name
    }

    [PublicAPI]
    public partial class Filters {
        public const string ManagementStreamFilterPattern  = @"^\$connectors\/([^\/]+)$";
        public const string CheckpointsStreamFilterPattern = @"^\$connectors\/[^\/]+\/checkpoints$";
        public const string LifecycleStreamFilterPattern   = @"^\$connectors\/[^\/]+\/lifecycle$";

        [GeneratedRegex(ManagementStreamFilterPattern)]  private static partial Regex GetManagementStreamFilterRegEx();
        [GeneratedRegex(CheckpointsStreamFilterPattern)] private static partial Regex GetCheckpointsStreamFilterRegEx();
        [GeneratedRegex(LifecycleStreamFilterPattern)]   private static partial Regex GetLifecycleStreamFilterRegEx();

        public static readonly ConsumeFilter ManagementFilter  = FromRegex(ConsumeFilterScope.Stream, GetManagementStreamFilterRegEx());
        public static readonly ConsumeFilter CheckpointsFilter = FromRegex(ConsumeFilterScope.Stream, GetCheckpointsStreamFilterRegEx());
        public static readonly ConsumeFilter LifecycleFilter   = FromRegex(ConsumeFilterScope.Stream, GetLifecycleStreamFilterRegEx());
    }

    public static async Task<RegisteredSchema> RegisterControlMessages<T>(ISchemaRegistry registry, CancellationToken token = default) {
        var schemaInfo = new SchemaInfo(Messages.GetControlSystemMessageSubject(typeof(T).Name), SchemaDefinitionType.Json);
        return await registry.RegisterSchema<T>(schemaInfo, cancellationToken: token);
    }

    public static async Task<RegisteredSchema> RegisterManagementMessages<T>(ISchemaRegistry registry, CancellationToken token = default) {
        var schemaInfo = new SchemaInfo(Messages.GetManagementMessageSubject(typeof(T).Name), SchemaDefinitionType.Json);
        return await registry.RegisterSchema<T>(schemaInfo, cancellationToken: token);
    }
}

public sealed record ManagementStreamTemplate()
    : StreamTemplate($"{ConnectorsFeatureConventions.Streams.StreamPrefix}/{{0}}");

public sealed record LeasesStreamTemplate()
    : StreamTemplate($"{ConnectorsFeatureConventions.Streams.StreamPrefix}/{{0}}/leases");

public sealed record CheckpointsStreamTemplate()
    : StreamTemplate($"{ConnectorsFeatureConventions.Streams.StreamPrefix}/{{0}}/checkpoints");

public sealed record LifecycleStreamTemplate()
    : StreamTemplate($"{ConnectorsFeatureConventions.Streams.StreamPrefix}/{{0}}/lifecycle");