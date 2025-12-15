// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public class CustomIndexReadsideService(
	IEventReader store,
	ISystemClient client,
	ISchemaSerializer serializer,
	CustomIndexEngine manager,
	CustomIndexStreamNameMap streamNameMap) {

	public async ValueTask<ListIndexesResponse> List(CancellationToken ct) {
		manager.EnsureLive();

		var state = new CustomIndexesState();

		await foreach (var evt in client.Reading.ReadIndexForwards(
			CustomIndexConstants.ManagementStream,
			Position.Start,
			maxCount: long.MaxValue,
			ct)) {

			var deserializedEvent = await serializer.Deserialize(
				data: evt.Event.Data,
				schemaInfo: new(evt.Event.EventType, SchemaDataFormat.Json));

			var customIndexName = CustomIndexHelpers.ParseManagementStreamName(evt.Event.EventStreamId);

			state.When(new CustomIndexId(customIndexName), deserializedEvent!);
		}

		return state.Convert();
	}

	public async ValueTask<GetIndexResponse> Get(string name, CancellationToken ct) {
		manager.EnsureLive();

		var streamName = streamNameMap.GetStreamName<CustomIndexId>(new(name));

		var state = await store.LoadState<CustomIndexState>(
			streamName: streamName,
			failIfNotFound: false,
			cancellationToken: ct);

		if (state.State.Status
			is IndexStatus.Unspecified
			or IndexStatus.Deleted)
			throw new UserIndexNotFoundException(name);

		return new() {
			Index = state.State.Convert(),
		};
	}

	public record CustomIndexesState : MultiEntityState<CustomIndexesState, CustomIndexId> {
		public Dictionary<string, CustomIndexState> CustomIndexes { get; } = [];

		public CustomIndexesState() {
			On<IndexCreated>((state, customIndexId, evt) => {
				CustomIndexes[customIndexId.Name] = new CustomIndexState().When(evt);
				return this;
			});

			On<IndexStarted>((state, customIndexId, evt) => {
				if (CustomIndexes.TryGetValue(customIndexId.Name, out var customIndexState))
					CustomIndexes[customIndexId.Name] = customIndexState.When(evt);
				return this;
			});

			On<IndexStopped>((state, customIndexId, evt) => {
				if (CustomIndexes.TryGetValue(customIndexId.Name, out var customIndexState))
					CustomIndexes[customIndexId.Name] = customIndexState.When(evt);
				return this;
			});

			On<IndexDeleted>((state, customIndexId, evt) => {
				CustomIndexes.Remove(customIndexId.Name);
				return this;
			});
		}
	}

	public record CustomIndexState : State<CustomIndexState> {
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

	// Similar to Eventuous State but handles events for different entities. The Identity is passed in with the event.
	public abstract record MultiEntityState<T, TId> where T : MultiEntityState<T, TId> where TId : Id {
		readonly Dictionary<Type, Func<T, TId, object, T>> _handlers = [];

		public virtual T When(TId stream, object evt) {
			var eventType = evt.GetType();

			if (!_handlers.TryGetValue(eventType, out var handler))
				return (T)this;

			return handler((T)this, stream, evt);
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
	public static Protocol.V2.Indexes.Index Convert(this CustomIndexReadsideService.CustomIndexState self) => new() {
		Filter = self.Filter,
		Fields = { self.Fields },
		Status = self.Status,
	};

	public static ListIndexesResponse Convert(this CustomIndexReadsideService.CustomIndexesState self) {
		var result = new ListIndexesResponse();
		foreach (var (name, customIndex) in self.CustomIndexes)
			result.Indexes[name] = customIndex.Convert();
		return result;
	}
}
