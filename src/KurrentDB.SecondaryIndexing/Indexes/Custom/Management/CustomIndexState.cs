// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public record CustomIndexState : State<CustomIndexState, CustomIndexId> {
	public string EventFilter { get; init; } = "";
	public string PartitionKeySelector { get; init; } = "";
	public PartitionKeyType PartitionKeyType { get; init; }
	public CustomIndexStatus Status { get; init; }

	public CustomIndexState() {
		On<CustomIndexEvents.Created>((state, evt) =>
			state with {
				EventFilter = evt.EventFilter,
				PartitionKeySelector = evt.PartitionKeySelector,
				PartitionKeyType = evt.PartitionKeyType,
				Status = CustomIndexStatus.Stopped,
			});

		On<CustomIndexEvents.Started>((state, evt) =>
			state with { Status = CustomIndexStatus.Started });

		On<CustomIndexEvents.Stopped>((state, evt) =>
			state with { Status = CustomIndexStatus.Stopped });

		On<CustomIndexEvents.Deleted>((state, evt) =>
			state with { Status = CustomIndexStatus.Deleted });
	}

	public enum CustomIndexStatus {
		NonExistent,
		Stopped,
		Started,
		Deleted,
	}
}
