// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

// The name of this class drives the custom index stream names
public class CustomIndex : Aggregate<CustomIndexState> {
	public void Create(CreateIndexRequest cmd) {
		var start = !cmd.HasStart || cmd.Start;

		switch (State.Status) {
			case IndexStatus.Unspecified: {
				// new
				CreateCustomIndex();
				break;
			}
			case IndexStatus.Stopped:
			case IndexStatus.Started: {
				// already exists
				if (State.Status is IndexStatus.Stopped && start ||
					State.Status is IndexStatus.Started && !start ||
					!State.Filter.Equals(cmd.Filter) ||
					!State.Fields.Equals(cmd.Fields))
					throw new UserIndexAlreadyExistsException(State.Id.Name);

				break; // idempotent
			}

			case IndexStatus.Deleted: {
				CreateCustomIndex();
				break;
			}
		}

		return;

		void CreateCustomIndex() {
			Apply(new IndexCreated {
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
			case IndexStatus.Unspecified:
			case IndexStatus.Deleted:
				throw new UserIndexNotFoundException(State.Id.Name);
			case IndexStatus.Stopped:
				Apply(new IndexStarted { Timestamp = DateTime.UtcNow.ToTimestamp() });
				break;
			case IndexStatus.Started:
				break; // idempotent
		}
	}

	public void Stop() {
		switch (State.Status) {
			case IndexStatus.Unspecified:
			case IndexStatus.Deleted:
				throw new UserIndexNotFoundException(State.Id.Name);
			case IndexStatus.Stopped:
				break; // idempotent
			case IndexStatus.Started:
				Apply(new IndexStopped { Timestamp = DateTime.UtcNow.ToTimestamp() });
				break;
		}
	}

	public void Delete() {
		if (State.Status is IndexStatus.Deleted)
			return; // idempotent

		if (State.Status is IndexStatus.Unspecified)
			throw new UserIndexNotFoundException(State.Id.Name);

		if (State.Status is IndexStatus.Started)
			Stop();

		Apply(new IndexDeleted { Timestamp = DateTime.UtcNow.ToTimestamp() });
	}
}
