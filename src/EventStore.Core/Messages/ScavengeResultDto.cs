// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Messages;

public class ScavengeResultDto {
	public string ScavengeId { get; set; }

	public ScavengeResultDto() {
	}

	public ScavengeResultDto(string scavengeId) {
		ScavengeId = scavengeId;
	}

	public override string ToString() => $"ScavengeId: {ScavengeId}";
}
