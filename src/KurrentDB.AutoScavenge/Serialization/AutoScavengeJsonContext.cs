// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;
using KurrentDB.AutoScavenge.Clients;
using KurrentDB.AutoScavenge.Domain;
using KurrentDB.AutoScavenge.Scavengers;

namespace KurrentDB.AutoScavenge.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Unit))]
[JsonSerializable(typeof(AutoScavengePlugin.AutoScavengeConfigurationPayload))]
[JsonSerializable(typeof(AutoScavengePlugin.GetAutoScavengeEnabledResult))]
[JsonSerializable(typeof(AutoScavengeStatusResponse))]
[JsonSerializable(typeof(IEvent))]
[JsonSerializable(typeof(Events.ConfigurationUpdated))]
[JsonSerializable(typeof(Events.ClusterMembersChanged))]
[JsonSerializable(typeof(Events.ClusterScavengeStarted))]
[JsonSerializable(typeof(Events.ClusterScavengeCompleted))]
[JsonSerializable(typeof(Events.NodeDesignated))]
[JsonSerializable(typeof(Events.NodeScavengeStarted))]
[JsonSerializable(typeof(Events.NodeScavengeCompleted))]
[JsonSerializable(typeof(Events.Initialized))]
[JsonSerializable(typeof(Events.PauseRequested))]
[JsonSerializable(typeof(Events.Paused))]
[JsonSerializable(typeof(Events.Resumed))]
[JsonSerializable(typeof(HttpNodeScavenger.ScavengeRecord))]
[JsonSerializable(typeof(HttpNodeScavenger.ScavengeRecordCompleted))]
[JsonSerializable(typeof(HttpNodeScavenger.LastScavengeStatusResponse))]
[JsonSerializable(typeof(ProxyAutoScavengeClient.ConfigureRequest))]
internal partial class AutoScavengeJsonContext : JsonSerializerContext;
