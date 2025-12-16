// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.SecondaryIndexing.Indexes.User.Management;

public record UserIndexState : State<UserIndexState, UserIndexId> {
	public string Filter { get; init; } = "";
	public IList<IndexField> Fields { get; init; } = [];
	public IndexState State { get; init; }

	public UserIndexState() {
		On<IndexCreated>((state, evt) =>
			state with {
				Filter = evt.Filter,
				Fields = evt.Fields,
				State = IndexState.Stopped,
			});

		On<IndexStarted>((state, evt) =>
			state with { State = IndexState.Started });

		On<IndexStopped>((state, evt) =>
			state with { State = IndexState.Stopped });

		On<IndexDeleted>((state, evt) =>
			state with { State = IndexState.Deleted });
	}
}
