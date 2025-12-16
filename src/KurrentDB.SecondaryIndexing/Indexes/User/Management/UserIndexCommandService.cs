// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.SecondaryIndexing.Indexes.User.Management;

public record UserIndexId(string Name) : Id(Name);

public class UserIndexCommandService : CommandService<UserIndex, UserIndexState, UserIndexId> {
	public UserIndexCommandService(IEventStore store, UserIndexStreamNameMap streamNameMap)
		: base(store: store, streamNameMap: streamNameMap) {

		On<CreateIndexRequest>()
			.InState(ExpectedState.Any) // facilitate idempotent create
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Create(cmd));

		On<StartIndexRequest>()
			.InState(ExpectedState.Any) // facilitate throwing our own exceptions if not existing
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Start());

		On<StopIndexRequest>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Stop());

		On<DeleteIndexRequest>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Delete());
	}
}
