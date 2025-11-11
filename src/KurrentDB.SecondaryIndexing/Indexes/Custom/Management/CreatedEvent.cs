// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public struct CreatedEvent {
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("filter")]
	public string EventFilter { get; set; }

	[JsonPropertyName("key-selector")]
	public string? PartitionKeySelector { get; set; }

	[JsonPropertyName("key-type")]
	public string? PartitionKeyType { get; set; }

}
