// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge;
using Kurrent.Surge.Schema;

namespace KurrentDB.Connectors.Planes.Management.Queries;

[PublicAPI]
public partial class ConnectorQueryConventions {
    [PublicAPI]
    public static class Streams {
        public static readonly StreamId ConnectorsStateProjectionStream            = "$connectors-mngt/state-projection";
        public static readonly StreamId ConnectorsStateProjectionCheckpointsStream = "$connectors-mngt/state-projection/checkpoints";
    }

    public static async Task<RegisteredSchema> RegisterQueryMessages<T>(ISchemaRegistry registry, CancellationToken token = default) {
        var schemaInfo = new SchemaInfo(
            ConnectorsFeatureConventions.Messages.GetManagementMessageSubject(typeof(T).Name),
            SchemaDataFormat.Json
        );

        return await registry.RegisterSchema<T>(schemaInfo, cancellationToken: token);
    }
}
