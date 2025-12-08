// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

// The name of this class drives the custom index stream names
public class CustomIndex : Aggregate<CustomIndexState> {
	public void Create(
		string filter,
		string valueSelector,
		CustomIndexValueType valueType,
		bool start) {

		switch (State.Status) {
			case CustomIndexState.CustomIndexStatus.NonExistent: {
				// new
				CreateCustomIndex();
				break;
			}
			case CustomIndexState.CustomIndexStatus.Stopped:
			case CustomIndexState.CustomIndexStatus.Started: {
				// already exists
				if (State.Status is CustomIndexState.CustomIndexStatus.Stopped && start ||
					State.Status is CustomIndexState.CustomIndexStatus.Started && !start ||
					State.Filter != filter ||
					State.ValueSelector != valueSelector ||
					State.ValueType != valueType)
					throw new CustomIndexAlreadyExistsException(State.Id.Name);

				break; // idempotent
			}

			case CustomIndexState.CustomIndexStatus.Deleted: {
				CreateCustomIndex();
				break;
			}
		}

		return;

		void CreateCustomIndex() {
			Apply(new CustomIndexEvents.Created {
				Filter = filter,
				ValueSelector = valueSelector,
				ValueType = valueType,
			});

			if (start) {
				Start();
			}
		}
	}

	public void Start() {
		switch (State.Status) {
			case CustomIndexState.CustomIndexStatus.NonExistent:
			case CustomIndexState.CustomIndexStatus.Deleted:
				throw new CustomIndexNotFoundException(State.Id.Name);
			case CustomIndexState.CustomIndexStatus.Stopped:
				Apply(new CustomIndexEvents.Started());
				break;
			case CustomIndexState.CustomIndexStatus.Started:
				break; // idempotent
		}
	}

	public void Stop() {
		switch (State.Status) {
			case CustomIndexState.CustomIndexStatus.NonExistent:
			case CustomIndexState.CustomIndexStatus.Deleted:
				throw new CustomIndexNotFoundException(State.Id.Name);
			case CustomIndexState.CustomIndexStatus.Stopped:
				break; // idempotent
			case CustomIndexState.CustomIndexStatus.Started:
				Apply(new CustomIndexEvents.Stopped());
				break;
		}
	}

	public void Delete() {
		if (State.Status is CustomIndexState.CustomIndexStatus.Deleted)
			return; // idempotent

		if (State.Status is CustomIndexState.CustomIndexStatus.NonExistent)
				throw new CustomIndexNotFoundException(State.Id.Name);

		if (State.Status is CustomIndexState.CustomIndexStatus.Started)
			Stop();

		Apply(new CustomIndexEvents.Deleted());
	}
}
