// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Testing;
using TUnit.Core.Interfaces;

namespace KurrentDB.Ammeter;

public record EnvironmentParallelLimit : IParallelLimit {
	public int Limit {
		get {
			var limit = ToolkitTestEnvironment.Configuration["ParallelLimit"]?.Apply(int.Parse);
			return limit is null or -1
				? Environment.ProcessorCount
				: limit.Value;
		}
	}
}
