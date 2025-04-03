// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Messages;

public class ScavengeGetCurrentResultDto {
	public string ScavengeId { get; set; } = "";
	public string ScavengeLink { get; set; } = "";

	public override string ToString() =>
		$"ScavengeId: {ScavengeId}, " +
		$"ScavengeLink: {ScavengeLink}";
}
