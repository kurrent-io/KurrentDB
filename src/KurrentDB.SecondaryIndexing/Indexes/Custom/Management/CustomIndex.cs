// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Grpc.Core;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

// The name of this class drives the custom index stream names
public class CustomIndex : Aggregate<CustomIndexState> {
	public void Create(
		string eventFilter,
		string partitionKeySelector,
		PartitionKeyType partitionKeyType,
		bool enable,
		bool force) {

		switch (State.Status) {
			case CustomIndexState.CustomIndexStatus.NonExistent: {
				// new
				CreateCustomIndex();
				break;
			}
			case CustomIndexState.CustomIndexStatus.Disabled:
			case CustomIndexState.CustomIndexStatus.Enabled: {
				// already exists
				if (State.Status is CustomIndexState.CustomIndexStatus.Disabled && enable ||
					State.Status is CustomIndexState.CustomIndexStatus.Enabled && !enable ||
					State.EventFilter != eventFilter ||
					State.PartitionKeySelector != partitionKeySelector ||
					State.PartitionKeyType != partitionKeyType)
					throw new CustomIndexException(StatusCode.AlreadyExists, "Custom Index already exists");

				break; // idempotent
			}

			case CustomIndexState.CustomIndexStatus.Deleted: {
				if (!force)
					throw new CustomIndexException(StatusCode.FailedPrecondition, "Custom Index cannot be recreated unless forced");
				CreateCustomIndex();
				break;
			}
		}

		return;

		void CreateCustomIndex() {
			Apply(new CustomIndexEvents.Created {
				EventFilter = eventFilter,
				PartitionKeySelector = partitionKeySelector,
				PartitionKeyType = partitionKeyType,
			});

			if (enable) {
				Enable();
			}
		}
	}

	public void Enable() {
		switch (State.Status) {
			case CustomIndexState.CustomIndexStatus.NonExistent:
			case CustomIndexState.CustomIndexStatus.Deleted:
				throw new CustomIndexException(StatusCode.NotFound, "Custom Index does not exist");
			case CustomIndexState.CustomIndexStatus.Disabled:
				Apply(new CustomIndexEvents.Enabled());
				break;
			case CustomIndexState.CustomIndexStatus.Enabled:
				break; // idempotent
		}
	}

	public void Disable() {
		switch (State.Status) {
			case CustomIndexState.CustomIndexStatus.NonExistent:
			case CustomIndexState.CustomIndexStatus.Deleted:
				throw new CustomIndexException(StatusCode.NotFound, "Custom Index does not exist");
			case CustomIndexState.CustomIndexStatus.Disabled:
				break; // idempotent
			case CustomIndexState.CustomIndexStatus.Enabled:
				Apply(new CustomIndexEvents.Disabled());
				break;
		}
	}

	public void Delete() {
		if (State.Status is CustomIndexState.CustomIndexStatus.Deleted)
			return; // idempotent

		if (State.Status is CustomIndexState.CustomIndexStatus.NonExistent)
			throw new CustomIndexException(StatusCode.NotFound, "Custom Index does not exist");

		if (State.Status is CustomIndexState.CustomIndexStatus.Enabled)
			Disable();

		Apply(new CustomIndexEvents.Deleted());
	}
}
