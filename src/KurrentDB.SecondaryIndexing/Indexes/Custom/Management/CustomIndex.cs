// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Protocol.V2.CustomIndexes;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

// The name of this class drives the custom index stream names
public class CustomIndex : Aggregate<CustomIndexState> {
	public void Create(CreateCustomIndexRequest cmd) {
		var start = !cmd.HasStart || cmd.Start;

		switch (State.Status) {
			case CustomIndexStatus.Unspecified: {
				// new
				CreateCustomIndex();
				break;
			}
			case CustomIndexStatus.Stopped:
			case CustomIndexStatus.Started: {
				// already exists
				if (State.Status is CustomIndexStatus.Stopped && start ||
					State.Status is CustomIndexStatus.Started && !start ||
					!State.Filter.Equals(cmd.Filter) ||
					!State.Fields.Equals(cmd.Fields))
					throw new CustomIndexAlreadyExistsException(State.Id.Name);

				break; // idempotent
			}

			case CustomIndexStatus.Deleted: {
				CreateCustomIndex();
				break;
			}
		}

		return;

		void CreateCustomIndex() {
			Apply(new CustomIndexCreated {
				Timestamp = DateTime.UtcNow.ToTimestamp(),
				Filter = cmd.Filter,
				Fields = { cmd.Fields },
			});

			if (start) {
				Start();
			}
		}
	}

	public void Start() {
		switch (State.Status) {
			case CustomIndexStatus.Unspecified:
			case CustomIndexStatus.Deleted:
				throw new CustomIndexNotFoundException(State.Id.Name);
			case CustomIndexStatus.Stopped:
				Apply(new CustomIndexStarted { Timestamp = DateTime.UtcNow.ToTimestamp() });
				break;
			case CustomIndexStatus.Started:
				break; // idempotent
		}
	}

	public void Stop() {
		switch (State.Status) {
			case CustomIndexStatus.Unspecified:
			case CustomIndexStatus.Deleted:
				throw new CustomIndexNotFoundException(State.Id.Name);
			case CustomIndexStatus.Stopped:
				break; // idempotent
			case CustomIndexStatus.Started:
				Apply(new CustomIndexStopped { Timestamp = DateTime.UtcNow.ToTimestamp() });
				break;
		}
	}

	public void Delete() {
		if (State.Status is CustomIndexStatus.Deleted)
			return; // idempotent

		if (State.Status is CustomIndexStatus.Unspecified)
			throw new CustomIndexNotFoundException(State.Id.Name);

		if (State.Status is CustomIndexStatus.Started)
			Stop();

		Apply(new CustomIndexDeleted { Timestamp = DateTime.UtcNow.ToTimestamp() });
	}
}
