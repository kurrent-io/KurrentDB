// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;

namespace KurrentDB.AutoScavenge.Serialization;

// Gossip messages are read from KurrentDB.Core's own $mem-gossip encoding, which uses default
// (PascalCase) property names rather than the camelCase used by AutoScavenge's own wire types.
[JsonSerializable(typeof(GossipMessage))]
internal partial class GossipJsonContext : JsonSerializerContext;
