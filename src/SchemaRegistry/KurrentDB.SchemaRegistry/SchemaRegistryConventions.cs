// ReSharper disable MemberHidesStaticFromOuterClass

using System.Text.RegularExpressions;
using Eventuous;
using Humanizer;
using Kurrent.Surge;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Schema;

using static Kurrent.Surge.Consumers.ConsumeFilter;

namespace KurrentDB.SchemaRegistry;

[PublicAPI]
public partial class SchemaRegistryConventions {
    [PublicAPI]
    public static class Streams {
        public const string RegistryStreamPrefix = "$registry";
        public const string SchemasStreamPrefix  = $"{RegistryStreamPrefix}/schemas";
        public const string GroupsStreamPrefix   = $"{RegistryStreamPrefix}/groups";

        public static readonly StreamTemplate SchemasStreamTemplate = new($"{SchemasStreamPrefix}/{{0}}");
        public static readonly StreamTemplate GroupsStreamTemplate  = new($"{GroupsStreamPrefix}/{{0}}");
    }

    [PublicAPI]
    public partial class Filters {
        [GeneratedRegex($@"^\{Streams.RegistryStreamPrefix}/")]
        private static partial Regex GetRegistryStreamFilterRegEx();

        [GeneratedRegex($@"^\{Streams.SchemasStreamPrefix}/")]
        private static partial Regex GetSchemasStreamFilterRegEx();

        [GeneratedRegex($@"^\{Streams.GroupsStreamPrefix}/")]
        private static partial Regex GetGroupsStreamFilterRegEx();

        public static readonly ConsumeFilter RegistryFilter = FromRegex(ConsumeFilterScope.Stream, GetRegistryStreamFilterRegEx());
        public static readonly ConsumeFilter SchemasFilter  = FromRegex(ConsumeFilterScope.Stream, GetSchemasStreamFilterRegEx());
        public static readonly ConsumeFilter GroupsFilter   = FromRegex(ConsumeFilterScope.Stream, GetGroupsStreamFilterRegEx());
    }

    public static async Task<RegisteredSchema> RegisterMessages<T>(ISchemaRegistry client, CancellationToken ct = default) {
        var schemaName = $"{Streams.RegistryStreamPrefix}-{typeof(T).Name.Kebaberize()}";

        AddEventuousTypeMapping(schemaName);

        return await client.RegisterSchema<T>(
            new SchemaInfo(schemaName, SchemaDataFormat.Json),
            cancellationToken: ct
        );

        static void AddEventuousTypeMapping(string schemaName) =>
            TypeMap.Instance.AddType<T>(schemaName);
    }
}