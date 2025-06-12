// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Processors;

namespace KurrentDB.Surge.Processors;

public class SystemProcessor(SystemProcessorOptions options) : Processor(options) {
	public static SystemProcessorBuilder Builder => new();
}
