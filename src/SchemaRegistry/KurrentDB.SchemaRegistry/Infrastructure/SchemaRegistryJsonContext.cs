// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;
using KurrentDB.SchemaRegistry.Infrastructure.System.Node.NodeSystemInfo;

namespace KurrentDB.SchemaRegistry.Infrastructure;

// UseStringEnumConverter matches the wire format the gossip stream is written in (enums as strings),
// e.g. ClientClusterInfo.ClientMemberInfo.State (a KurrentDB.Core enum we can't attribute directly).
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(IDictionary<string, string>))]
[JsonSerializable(typeof(IDictionary<string, string[]>))]
[JsonSerializable(typeof(NodeSystemInfoProviderExtensions.GossipUpdatedInMemory))]
internal partial class SchemaRegistryJsonContext : JsonSerializerContext;
