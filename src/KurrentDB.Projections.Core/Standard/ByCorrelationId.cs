// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Services;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KurrentDB.Projections.Core.Standard;

public class ByCorrelationId : IProjectionStateHandler {
	private readonly string _corrIdStreamPrefix;

	// ReSharper disable once UnusedParameter.Local
	public ByCorrelationId(string source, Action<string, object[]> logger) {
		if (!string.IsNullOrWhiteSpace(source)) {
			if (!TryParseCorrelationIdProperty(source, out var correlationIdProperty)) {
				throw new InvalidOperationException(
					"Could not parse projection source. Please make sure the source is a valid JSON string with a property: 'correlationIdProperty' having a string value");
			}

			CorrelationIdPropertyContext.CorrelationIdProperty = correlationIdProperty;
		}

		_corrIdStreamPrefix = "$bc-";
	}

	private static bool TryParseCorrelationIdProperty(string source, out string correlationIdProperty) {
		correlationIdProperty = null;
		try {
			var obj = JObject.Parse(source);
			string prop = obj["correlationIdProperty"].Value<string>();
			if (prop != null) {
				correlationIdProperty = prop;
				return true;
			}

			return false;
		} catch (Exception) {
			return false;
		}
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
		string partition,
		CheckpointTag eventPosition,
		string category1,
		ResolvedEvent data,
		out string newState,
		out string newSharedState,
		out EmittedEventEnvelope[] emittedEvents) {
		newSharedState = null;
		emittedEvents = null;
		newState = null;
		if (data.EventStreamId != data.PositionStreamId)
			return false;

		if (data.Metadata == null)
			return false;

		JObject metadata;

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

		var linkTarget = data.EventType == SystemEventTypes.LinkTo ? data.Data : $"{data.EventSequenceNumber}@{data.EventStreamId}";

		var metadataDict = new Dictionary<string, string> { { "$eventTimestamp", $"\"{data.Timestamp:yyyy-MM-ddTHH:mm:ss.ffffffZ}\"" } };
		if (data.EventType == SystemEventTypes.LinkTo) {
			JObject linkObj = new JObject {
				{ "eventId", data.EventId },
				{ "metadata", metadata }
			};
			metadataDict.Add("$link", linkObj.ToJson());
		}

		var linkMetadata = new ExtraMetaData(metadataDict);

		emittedEvents = [
			new(new EmittedDataEvent(
				_corrIdStreamPrefix + correlationId,
				Guid.NewGuid(),
				"$>",
				false,
				linkTarget,
				linkMetadata,
				eventPosition,
				expectedTag: null))
		];

		return true;
	}

	public bool ProcessPartitionCreated(string partition,
		CheckpointTag createPosition,
		ResolvedEvent data,
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

		static void ConfigureSourceProcessingStrategy(SourceDefinitionBuilder builder) {
			builder.FromAll();
			builder.AllEvents();
		}
	}
}
