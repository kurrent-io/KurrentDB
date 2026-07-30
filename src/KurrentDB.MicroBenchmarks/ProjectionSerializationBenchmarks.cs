// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using BenchmarkDotNet.Attributes;
using Jint;
using Jint.Native;
using Jint.Native.Json;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services.Interpreted;
using KurrentDB.Projections.Core.Tests.Services.Jint.Serialization;

namespace KurrentDB.MicroBenchmarks;

/// <summary>
/// Measures the per-event state serialization a JS projection pays on every handled event. This used to
/// be an A/B of a hand-written serializer against Jint's; the hand-written one is gone, so what is left
/// is the production path.
/// </summary>
[MemoryDiagnoser]
public class ProjectionSerializationBenchmarks {
	private readonly JintProjectionStateHandler _handler;
	private readonly JsValue _stateInstance;

	public ProjectionSerializationBenchmarks() {
		var json = when_serializing_state.ReadJsonFromFile("big_state.json");

		var engine = new Engine();
		var parser = new JsonParser(engine);
		_handler = new JintProjectionStateHandler("", false, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500),
			new(IProjectionExecutionTracker.NoOp), new(IProjectionStateSerializationTracker.NoOp));

		_stateInstance = parser.Parse(json);
	}

	[Benchmark]
	public void SerializeState() {
		var s = _handler.Serialize(_stateInstance);
		if (string.IsNullOrEmpty(s))
			throw new Exception("something went wrong");
	}
}
