// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

public class FakeBiStateProjection : IProjectionStateHandler {
	private readonly string _query;
	private readonly Action<string, object[]> _logger;

	public FakeBiStateProjection(string query, Action<string, object[]> logger) {
		_query = query;
		_logger = logger;
	}

	public void Dispose() {
	}

	private void Log(string msg, params object[] args) {
		_logger(msg, args);
	}

	public void ConfigureSourceProcessingStrategy(SourceDefinitionBuilder builder) {
		Log("ConfigureSourceProcessingStrategy(" + builder + ")");
		builder.FromAll();
		builder.AllEvents();
		builder.SetByStream();
		builder.SetIsBiState(true);
	}

	public void Load(string state) {
		Log("Load(" + state + ")");
	}

	public void LoadShared(string state) {
		Log("LoadShared(" + state + ")");
	}

	public void Initialize() {
		Log("Initialize");
	}

	public void InitializeShared() {
		Log("InitializeShared");
	}

	public string GetStatePartition(CheckpointTag eventPosition, string category, ResolvedEvent data) {
		Log("GetStatePartition(" + "..." + ")");
		throw new NotImplementedException();
	}

	public bool ProcessEvent(
		string partition, CheckpointTag eventPosition, string category1, ResolvedEvent data, out string newState,
		out string newSharedState, out EmittedEventEnvelope[] emittedEvents) {
		newSharedState = null;
		if (data.EventType == "fail" || _query == "fail")
			throw new Exception("failed");
		Log("ProcessEvent(" + "..." + ")");
		newState = "{\"data\": 1}";
		newSharedState = "{\"data\": 2}";
		emittedEvents = null;
		return true;
	}

	public bool ProcessPartitionCreated(string partition, CheckpointTag createPosition, ResolvedEvent data,
		out EmittedEventEnvelope[] emittedEvents) {
		Log("ProcessPartitionCreated");
		emittedEvents = null;
		return false;
	}

	public bool ProcessPartitionDeleted(string partition, CheckpointTag deletePosition, out string newState) {
		throw new NotImplementedException();
	}

	public string TransformStateToResult() {
		throw new NotImplementedException();
	}

	public IQuerySources GetSourceDefinition() {
		return SourceDefinitionBuilder.From(ConfigureSourceProcessingStrategy);
	}
}
