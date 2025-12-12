// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using KurrentDB.Protocol.V2.CustomIndexes;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public record CustomIndexId(string Name) : Id(Name);

public class CustomIndexCommandService : CommandService<CustomIndex, CustomIndexState, CustomIndexId> {
	public CustomIndexCommandService(IEventStore store, CustomIndexStreamNameMap streamNameMap)
		: base(store: store, streamNameMap: streamNameMap) {

		On<CreateCustomIndexRequest>()
			.InState(ExpectedState.Any) // facilitate idempotent create
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Create(cmd));

		On<StartCustomIndexRequest>()
			.InState(ExpectedState.Any) // facilitate throwing our own exceptions if not existing
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Start());

		On<StopCustomIndexRequest>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Stop());

		On<DeleteCustomIndexRequest>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Delete());
	}
}
