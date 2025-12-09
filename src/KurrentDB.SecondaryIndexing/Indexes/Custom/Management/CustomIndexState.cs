// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using KurrentDB.Protocol.V2.CustomIndexes;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public record CustomIndexState : State<CustomIndexState, CustomIndexId> {
	public string Filter { get; init; } = "";
	public string PartitionKeySelector { get; init; } = "";
	public KeyType PartitionKeyType { get; init; }
	public CustomIndexStatus Status { get; init; }

	public CustomIndexState() {
		On<CustomIndexCreated>((state, evt) =>
			state with {
				Filter = evt.Filter,
				PartitionKeySelector = evt.PartitionKeySelector,
				PartitionKeyType = evt.PartitionKeyType,
				Status = CustomIndexStatus.Stopped,
			});

		On<CustomIndexStarted>((state, evt) =>
			state with { Status = CustomIndexStatus.Started });

		On<CustomIndexStopped>((state, evt) =>
			state with { Status = CustomIndexStatus.Stopped });

		On<CustomIndexDeleted>((state, evt) =>
			state with { Status = CustomIndexStatus.Deleted });
	}

	public enum CustomIndexStatus { //qq can remove
		NonExistent,
		Stopped,
		Started,
		Deleted,
	}
}
