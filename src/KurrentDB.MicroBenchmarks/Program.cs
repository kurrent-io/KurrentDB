// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace KurrentDB.MicroBenchmarks;

internal class Program {
	static void Main(string[] args) {
		var config = Debugger.IsAttached ? new DebugBuildConfig() { } : DefaultConfig.Instance;
		BenchmarkRunner.Run<ProjectionSerializationBenchmarks>(config, args);
//		BenchmarkRunner.Run<QueueBenchmarks>(config, args);
	}
}
