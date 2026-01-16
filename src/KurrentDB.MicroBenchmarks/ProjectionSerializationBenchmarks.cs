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

[MemoryDiagnoser]
public class ProjectionSerializationBenchmarks {
	private JsonSerializer _builtIn;
	private JintProjectionStateHandler _handler;
	private JsValue _stateInstance;

	public ProjectionSerializationBenchmarks() {
		var json = when_serializing_state.ReadJsonFromFile("big_state.json");

		var engine = new Engine();
		var parser = new JsonParser(engine);
		_builtIn = new JsonSerializer(engine);
		_handler = new JintProjectionStateHandler("", false, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500),
			new(IProjectionExecutionTracker.NoOp), new(IProjectionStateSerializationTracker.NoOp));

		_stateInstance = parser.Parse(json);

	}

	[Benchmark(Baseline = true)]
	public void JintSerializer() {
		var s = _builtIn.Serialize(_stateInstance, JsValue.Undefined, JsValue.Undefined).AsString();
		if (string.IsNullOrEmpty(s))
			throw new Exception("something went wrong");
	}

	[Benchmark]
	public void CustomSerializer() {
		var s = _handler.Serialize(_stateInstance);
		if (string.IsNullOrEmpty(s))
			throw new Exception("something went wrong");
	}
}
