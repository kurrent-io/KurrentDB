// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public record CustomIndexId(string Name) : Id(Name);

public class CustomIndexDomainService : CommandService<CustomIndex, CustomIndexState, CustomIndexId> {
	public CustomIndexDomainService(IEventStore store, CustomIndexStreamNameMap streamNameMap)
		: base(store: store, streamNameMap: streamNameMap) {

		On<CustomIndexCommands.Create>()
			.InState(ExpectedState.Any) // facilitate idempotent create
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Create(
				filter: cmd.EventFilter,
				valueSelector: cmd.ValueSelector,
				valueType: cmd.ValueType,
				start: cmd.Start));

		On<CustomIndexCommands.Start>()
			.InState(ExpectedState.Any) // facilitate throwing our own exceptions if not existing
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Start());

		On<CustomIndexCommands.Stop>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Stop());

		On<CustomIndexCommands.Delete>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Delete());
	}
}
