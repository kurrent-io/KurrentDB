// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Core;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.SecondaryIndexing.Indexes.User.Management;

public class UserIndexQueryService(
	IEventReader store,
	ISystemClient client,
	ISchemaSerializer serializer,
	UserIndexEngine engine,
	UserIndexStreamNameMap streamNameMap) {

	public async ValueTask<ListIndexesResponse> List(CancellationToken ct) {
		engine.EnsureLive();

		var state = new UserIndexesState();

		await foreach (var evt in client.Reading.ReadStreamForwards(
			UserIndexConstants.ManagementAllStream,
			StreamRevision.Start,
			maxCount: long.MaxValue,
			ct)) {

			var deserializedEvent = await serializer.Deserialize(
				data: evt.Event.Data,
				schemaInfo: new(evt.Event.EventType, SchemaDataFormat.Json));

			state.When(deserializedEvent!);
		}

		return state.Convert();
	}

	public async ValueTask<GetIndexResponse> Get(string name, CancellationToken ct) {
		engine.EnsureLive();

		var streamName = streamNameMap.GetStreamName<UserIndexId>(new(name));

		var state = await store.LoadState<UserIndexState>(
			streamName: streamName,
			failIfNotFound: false,
			cancellationToken: ct);

		if (state.State.State
			is IndexState.Unspecified
			or IndexState.Deleted)
			throw new UserIndexNotFoundException(name);

		return new() {
			Index = state.State.Convert(),
		};
	}

	public record UserIndexesState : State<UserIndexesState, UserIndexId> {
		public Dictionary<string, UserIndexState> UserIndexes { get; } = [];

		public UserIndexesState() {
			On<IndexCreated>((_, evt) => {
				UserIndexes[evt.Name] = new UserIndexState().When(evt);
				return this;
			});

			On<IndexStarted>((_, evt) => {
				if (UserIndexes.TryGetValue(evt.Name, out var userIndexState))
					UserIndexes[evt.Name] = userIndexState.When(evt);
				return this;
			});

			On<IndexStopped>((_, evt) => {
				if (UserIndexes.TryGetValue(evt.Name, out var userIndexState))
					UserIndexes[evt.Name] = userIndexState.When(evt);
				return this;
			});

			On<IndexDeleted>((_, evt) => {
				UserIndexes.Remove(evt.Name);
				return this;
			});
		}
	}

	public record UserIndexState : State<UserIndexState> {
		public string Name { get; init; } = "";
		public string Filter { get; init; } = "";
		public IList<IndexField> Fields { get; init; } = [];
		public IndexState State { get; init; }

		public UserIndexState() {
			On<IndexCreated>((state, evt) =>
				state with {
					Name = evt.Name,
					Filter = evt.Filter,
					Fields = evt.Fields,
					State = IndexState.Stopped,
				});

			On<IndexStarted>((state, _) =>
				state with { State = IndexState.Started });

			On<IndexStopped>((state, _) =>
				state with { State = IndexState.Stopped });

			On<IndexDeleted>((state, _) =>
				state with { State = IndexState.Deleted });
		}
	}
}

file static class Extensions {
	public static Protocol.V2.Indexes.Index Convert(this UserIndexQueryService.UserIndexState self) => new() {
		Name = self.Name,
		Filter = self.Filter,
		Fields = { self.Fields },
		State = self.State,
	};

	public static ListIndexesResponse Convert(this UserIndexQueryService.UserIndexesState self) {
		var result = new ListIndexesResponse();
		foreach (var (_, userIndex) in self.UserIndexes)
			result.Indexes.Add(userIndex.Convert());
		return result;
	}
}
