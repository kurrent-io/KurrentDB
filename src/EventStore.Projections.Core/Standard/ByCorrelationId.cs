// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Services;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EventStore.Common.Utils;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace EventStore.Projections.Core.Standard;

public class ByCorrelationId : IProjectionStateHandler {
	private readonly string _corrIdStreamPrefix;

	public ByCorrelationId(string source, Action<string, object[]> logger) {
		if (!string.IsNullOrWhiteSpace(source)) {
			string correlationIdProperty;
			if (!TryParseCorrelationIdProperty(source, out correlationIdProperty)) {
				throw new InvalidOperationException(
					"Could not parse projection source. Please make sure the source is a valid JSON string with a property: 'correlationIdProperty' having a string value");
			}

			CorrelationIdPropertyContext.CorrelationIdProperty = correlationIdProperty;
		}

		_corrIdStreamPrefix = "$bc-";
	}

	private bool TryParseCorrelationIdProperty(string source, out string correlationIdProperty) {
		correlationIdProperty = null;
		JObject obj = null;
		try {
			obj = JObject.Parse(source);
			string prop = obj["correlationIdProperty"].Value<string>();
			if (prop != null) {
				correlationIdProperty = prop;
				return true;
			} else {
				return false;
			}
		} catch (Exception) {
			return false;
		}
	}

	public void ConfigureSourceProcessingStrategy(SourceDefinitionBuilder builder) {
		builder.FromAll();
		builder.AllEvents();
	}

	public void Load(string state) {
	}

	public void LoadShared(string state) {
		throw new NotImplementedException();
	}

	public void Initialize() {
	}

	public void InitializeShared() {
	}

	public string GetStatePartition(CheckpointTag eventPosition, string category, ResolvedEvent data) {
		throw new NotImplementedException();
	}

	public bool ProcessEvent(
		string partition, CheckpointTag eventPosition, string category1, ResolvedEvent data,
		out string newState, out string newSharedState, out EmittedEventEnvelope[] emittedEvents) {
		newSharedState = null;
		emittedEvents = null;
		newState = null;
		if (data.EventStreamId != data.PositionStreamId)
			return false;

		if (data.Metadata == null)
			return false;

		JObject metadata = null;

		try {
			metadata = JObject.Parse(data.Metadata);
		} catch (JsonReaderException) {
			return false;
		}

		if (metadata[CorrelationIdPropertyContext.CorrelationIdProperty] == null)
			return false;

		string correlationId = metadata[CorrelationIdPropertyContext.CorrelationIdProperty].Value<string>();
		if (correlationId == null)
			return false;

		string linkTarget;
		if (data.EventType == SystemEventTypes.LinkTo)
			linkTarget = data.Data;
		else
			linkTarget = data.EventSequenceNumber + "@" + data.EventStreamId;

		var metadataDict = new Dictionary<string, string>();
		metadataDict.Add("$eventTimestamp", "\"" + data.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ") + "\"");
		if (data.EventType == SystemEventTypes.LinkTo) {
			JObject linkObj = new JObject();
			linkObj.Add("eventId", data.EventId);
			linkObj.Add("metadata", metadata);
			metadataDict.Add("$link", linkObj.ToJson());
		}

		var linkMetadata = new ExtraMetaData(metadataDict);

		emittedEvents = new[] {
			new EmittedEventEnvelope(
				new EmittedDataEvent(
					_corrIdStreamPrefix + correlationId, Guid.NewGuid(), "$>", false,
					linkTarget,
					linkMetadata,
					eventPosition,
					expectedTag: null))
		};


		return true;
	}

	public bool ProcessPartitionCreated(string partition, CheckpointTag createPosition, ResolvedEvent data,
		out EmittedEventEnvelope[] emittedEvents) {
		emittedEvents = null;
		return false;
	}

	public bool ProcessPartitionDeleted(string partition, CheckpointTag deletePosition, out string newState) {
		throw new NotImplementedException();
	}

	public string TransformStateToResult() {
		throw new NotImplementedException();
	}

	public void Dispose() {
	}

	public IQuerySources GetSourceDefinition() {
		return SourceDefinitionBuilder.From(ConfigureSourceProcessingStrategy);
	}
}
