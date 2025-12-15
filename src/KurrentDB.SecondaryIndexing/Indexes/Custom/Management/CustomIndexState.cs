// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public record CustomIndexState : State<CustomIndexState, CustomIndexId> {
	public string Filter { get; init; } = "";
	public IList<Field> Fields { get; init; } = [];
	public IndexStatus Status { get; init; }

	public CustomIndexState() {
		On<IndexCreated>((state, evt) =>
			state with {
				Filter = evt.Filter,
				Fields = evt.Fields,
				Status = IndexStatus.Stopped,
			});

		On<IndexStarted>((state, evt) =>
			state with { Status = IndexStatus.Started });

		On<IndexStopped>((state, evt) =>
			state with { Status = IndexStatus.Stopped });

		On<IndexDeleted>((state, evt) =>
			state with { Status = IndexStatus.Deleted });
	}
}
