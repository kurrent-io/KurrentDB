// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Common.Utils;
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

		await foreach (var evt in client.Reading.ReadIndexForwards(
			UserIndexConstants.ManagementStream,
			Position.Start,
			maxCount: long.MaxValue,
			ct)) {

			var deserializedEvent = await serializer.Deserialize(
				data: evt.Event.Data,
				schemaInfo: new(evt.Event.EventType, SchemaDataFormat.Json));

			var userIndexName = UserIndexHelpers.ParseManagementStreamName(evt.Event.EventStreamId);

			state.When(new UserIndexId(userIndexName), deserializedEvent!);
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

	public record UserIndexesState : MultiEntityState<UserIndexesState, UserIndexId> {
		public Dictionary<string, UserIndexState> UserIndexes { get; } = [];

		public UserIndexesState() {
			On<IndexCreated>((_, userIndexId, evt) => {
				UserIndexes[userIndexId.Name] = new UserIndexState().When(evt);
				return this;
			});

			On<IndexStarted>((_, userIndexId, evt) => {
				if (UserIndexes.TryGetValue(userIndexId.Name, out var userIndexState))
					UserIndexes[userIndexId.Name] = userIndexState.When(evt);
				return this;
			});

			On<IndexStopped>((_, userIndexId, evt) => {
				if (UserIndexes.TryGetValue(userIndexId.Name, out var userIndexState))
					UserIndexes[userIndexId.Name] = userIndexState.When(evt);
				return this;
			});

			On<IndexDeleted>((_, userIndexId, _) => {
				UserIndexes.Remove(userIndexId.Name);
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

	// Similar to Eventuous State but handles events for different entities. The Identity is passed in with the event.
	public abstract record MultiEntityState<T, TId> where T : MultiEntityState<T, TId> where TId : Id {
		readonly Dictionary<Type, Func<T, TId, object, T>> _handlers = [];

		public T When(TId stream, object evt) {
			var eventType = evt.GetType();

			return _handlers.TryGetValue(eventType, out var handler)
				? handler((T)this, stream, evt)
				: (T)this;
		}

		protected void On<TEvent>(Func<T, TId, TEvent, T> handle) {
			Ensure.NotNull(handle);

			if (!_handlers.TryAdd(typeof(TEvent), (state, stream, evt) => handle(state, stream, (TEvent)evt))) {
				throw new Exceptions.DuplicateTypeException<TEvent>();
			}
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
