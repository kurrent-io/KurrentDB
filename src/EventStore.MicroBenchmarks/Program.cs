// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using EventStore.Projections.Core.Services.Interpreted;
using EventStore.Projections.Core.Tests.Services.Jint.Serialization;
using Jint;
using Jint.Native;
using Jint.Native.Json;

namespace EventStore.MicroBenchmarks;

internal class Program {
	static void Main(string[] args) {
		var config = System.Diagnostics.Debugger.IsAttached ? new DebugBuildConfig(){ } : DefaultConfig.Instance;
		BenchmarkRunner.Run<ProjectionSerializationBenchmarks>(config, args);
	}
}

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
		_handler = new JintProjectionStateHandler("", false, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
		
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
