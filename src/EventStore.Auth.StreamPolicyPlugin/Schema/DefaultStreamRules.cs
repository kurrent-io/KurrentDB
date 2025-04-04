// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;

namespace EventStore.Auth.StreamPolicyPlugin.Schema;

public class DefaultStreamRules : IJsonOnDeserialized {
	[JsonRequired]
	public string UserStreams { get; init; } = default!;

	[JsonRequired]
	public string SystemStreams { get; init; } = default!;

	public void OnDeserialized() {
		if (string.IsNullOrEmpty(UserStreams))
			throw new ArgumentNullException($"{nameof(UserStreams)} cannot be null or empty");
		if (string.IsNullOrEmpty(SystemStreams))
			throw new ArgumentNullException($"{nameof(SystemStreams)} cannot be null or empty");
	}
}
