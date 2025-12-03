// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Services.Transport.Common;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public class CustomIndexReadsideService(
	IEventReader store,
	ISystemClient client,
	ISchemaSerializer serializer,
	CustomIndexStreamNameMap streamNameMap) {

	public async ValueTask<CustomIndexesState> List(CancellationToken ct) {
		var state = new CustomIndexesState();

		await foreach (var evt in client.Reading.ReadIndexForwards(
			CustomIndexConstants.ManagementStream,
			Position.Start,
			maxCount: long.MaxValue,
			ct)) {

			var deserializedEvent = await serializer.Deserialize(
				data: evt.Event.Data,
				schemaInfo: new(evt.Event.EventType, SchemaDataFormat.Json));

			//qq refactor, put place that writes and reads stream names together.
			var streamName = evt.Event.EventStreamId;
			var customIndexName = streamName[(streamName.IndexOf('-') + 1) ..];

			state.When(new CustomIndexId(customIndexName), deserializedEvent!);
		}

		return state;
	}

	public async ValueTask<CustomIndexState> Get(string name, CancellationToken ct) {
		var streamName = streamNameMap.GetStreamName<CustomIndexId>(new(name));

		var state = await store.LoadState<CustomIndexState>(
			streamName: streamName,
			failIfNotFound: false,
			cancellationToken: ct);

		return state.State;
	}

	public record CustomIndexesState : MultiEntityState<CustomIndexesState, CustomIndexId> {
		public Dictionary<string, CustomIndexState> CustomIndexes { get; } = [];

		public CustomIndexesState() {
			On<CustomIndexEvents.Created>((state, customIndexId, evt) => {
				CustomIndexes[customIndexId.Name] = new CustomIndexState().When(evt);
				return this;
			});

			On<CustomIndexEvents.Enabled>((state, customIndexId, evt) => {
				if (CustomIndexes.TryGetValue(customIndexId.Name, out var customIndexState))
					CustomIndexes[customIndexId.Name] = customIndexState.When(evt);
				return this;
			});

			On<CustomIndexEvents.Disabled>((state, customIndexId, evt) => {
				if (CustomIndexes.TryGetValue(customIndexId.Name, out var customIndexState))
					CustomIndexes[customIndexId.Name] = customIndexState.When(evt);
				return this;
			});

			On<CustomIndexEvents.Deleted>((state, customIndexId, evt) => {
				CustomIndexes.Remove(customIndexId.Name);
				return this;
			});
		}
	}

	public enum Status {
		None,
		Disabled,
		Enabled,
		Deleted,
	}

	public record CustomIndexState : State<CustomIndexState> {
		public string EventFilter { get; init; } = "";
		public string PartitionKeySelector { get; init; } = "";
		public PartitionKeyType PartitionKeyType { get; init; }
		public Status Status { get; init; }

		public CustomIndexState() {
			On<CustomIndexEvents.Created>((state, evt) =>
				state with {
					EventFilter = evt.EventFilter,
					PartitionKeySelector = evt.PartitionKeySelector,
					PartitionKeyType = evt.PartitionKeyType,
					Status = Status.Disabled,
				});

			On<CustomIndexEvents.Enabled>((state, evt) =>
				state with { Status = Status.Enabled });

			On<CustomIndexEvents.Disabled>((state, evt) =>
				state with { Status = Status.Disabled });

			On<CustomIndexEvents.Deleted>((state, evt) =>
				state with { Status = Status.Deleted });
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
