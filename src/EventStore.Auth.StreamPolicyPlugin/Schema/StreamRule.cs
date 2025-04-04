// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;

namespace EventStore.Auth.StreamPolicyPlugin.Schema;

public class StreamRule : IJsonOnDeserialized {
	[JsonRequired]
	public string StartsWith { get; init; } = default!;

	[JsonRequired]
	public string Policy { get; init; } = default!;
	public void OnDeserialized() {
		if (string.IsNullOrEmpty(StartsWith))
			throw new ArgumentNullException($"{nameof(StartsWith)} cannot be null or empty");
		if (string.IsNullOrEmpty(Policy))
			throw new ArgumentNullException($"{nameof(Policy)} cannot be null or empty");
	}
}
