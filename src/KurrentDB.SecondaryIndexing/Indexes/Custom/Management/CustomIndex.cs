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
		bool enabled,
		bool force) {

		switch (State.Status) {
			case CustomIndexStatus.NonExistent: {
				// new
				CreateCustomIndex();
				break;
			}
			case CustomIndexStatus.Disabled:
			case CustomIndexStatus.Enabled: {
				// already exists
				if (State.Status is CustomIndexStatus.Disabled && enabled ||
					State.Status is CustomIndexStatus.Enabled && !enabled ||
					State.EventFilter != eventFilter ||
					State.PartitionKeySelector != partitionKeySelector ||
					State.PartitionKeyType != partitionKeyType)
					throw new CustomIndexException(StatusCode.AlreadyExists, "Custom Index already exists");

				break; // idempotent
			}

			case CustomIndexStatus.Deleted: {
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

			if (enabled) {
				Enable();
			}
		}
	}

	public void Enable() {
		switch (State.Status) {
			case CustomIndexStatus.NonExistent:
			case CustomIndexStatus.Deleted:
				throw new CustomIndexException(StatusCode.NotFound, "Custom Index does not exist");
			case CustomIndexStatus.Disabled:
				Apply(new CustomIndexEvents.Enabled());
				break;
			case CustomIndexStatus.Enabled:
				break; // idempotent
		}
	}

	public void Disable() {
		switch (State.Status) {
			case CustomIndexStatus.NonExistent:
			case CustomIndexStatus.Deleted:
				throw new CustomIndexException(StatusCode.NotFound, "Custom Index does not exist");
			case CustomIndexStatus.Disabled:
				break; // idempotent
			case CustomIndexStatus.Enabled:
				Apply(new CustomIndexEvents.Disabled());
				break;
		}
	}

	public void Delete() {
		if (State.Status is CustomIndexStatus.Deleted)
			return; // idempotent

		if (State.Status is CustomIndexStatus.NonExistent)
			throw new CustomIndexException(StatusCode.NotFound, "Custom Index does not exist");

		if (State.Status is CustomIndexStatus.Enabled)
			Disable();

		Apply(new CustomIndexEvents.Deleted());
	}
}
