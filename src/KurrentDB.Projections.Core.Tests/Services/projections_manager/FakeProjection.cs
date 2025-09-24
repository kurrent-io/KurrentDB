// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

public class FakeProjection(string query, Action<string, object[]> logger) : IProjectionStateHandler {
	public void Dispose() {
	}

	private void Log(string msg, params object[] args) {
		logger(msg, args);
	}

	private void ConfigureSourceProcessingStrategy(SourceDefinitionBuilder builder) {
		Log($"ConfigureSourceProcessingStrategy({builder})");
		builder.FromAll();
		builder.AllEvents();
	}

	public void Load(string state) {
		Log($"Load({state})");
		throw new NotImplementedException();
	}

	public void LoadShared(string state) {
		throw new NotImplementedException();
	}

	public void Initialize() {
		Log("Initialize");
	}

	public void InitializeShared() {
		Log("InitializeShared");
	}

	public string GetStatePartition(CheckpointTag eventPosition, string category, ResolvedEvent data) {
		Log("GetStatePartition(...)");
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
		if (data.EventType == "fail" || query == "fail")
			throw new Exception("failed");
		Log("ProcessEvent(...)");
		newState = "{\"data\": 1}";
		emittedEvents = null;
		return true;
	}

	public bool ProcessPartitionCreated(string partition, CheckpointTag createPosition, ResolvedEvent data, out EmittedEventEnvelope[] emittedEvents) {
		Log("Process ProcessPartitionCreated");
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
