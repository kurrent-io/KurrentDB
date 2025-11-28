// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public record CustomIndexId(string Name) : Id(Name);

public class CustomIndexDomainService : CommandService<CustomIndex, CustomIndexState, CustomIndexId> {
	public CustomIndexDomainService(IEventStore store)
		: base(store: store, streamNameMap: CreateStreamNameMap()) {

		On<CustomIndexCommands.Create>()
			.InState(ExpectedState.Any) // facilitate idempotent create
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Create(
				eventFilter: cmd.EventFilter,
				partitionKeySelector: cmd.PartitionKeySelector,
				partitionKeyType: cmd.PartitionKeyType,
				enabled: cmd.Enabled,
				force: cmd.Force));

		On<CustomIndexCommands.Enable>()
			.InState(ExpectedState.Any) // facilitate throwing our own exceptions if not existing
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Enable());

		On<CustomIndexCommands.Disable>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Disable());

		On<CustomIndexCommands.Delete>()
			.InState(ExpectedState.Any)
			.GetId(cmd => new(cmd.Name))
			.Act((x, cmd) => x.Delete());
	}

	static StreamNameMap CreateStreamNameMap() {
		var streamNameMap = new StreamNameMap();
		streamNameMap.Register<CustomIndexId>(id => new StreamName($"{CustomIndexConstants.Category}-{id}"));
		return streamNameMap;
	}
}
