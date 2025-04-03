// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Messages;

public class MergeIndexesResultDto {
	public string MergeIndexesId { get; set; }

	public MergeIndexesResultDto() {
	}

	public MergeIndexesResultDto(string mergeIndexesId) {
		MergeIndexesId = mergeIndexesId;
	}
	public override string ToString() => $"MergeIndexesId: {MergeIndexesId}";
}
