// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.MD5;

public interface IMD5Plugin {
	string Name { get; }
	string Version { get; }
	string CommandLineName { get; }

	/// <summary>
	///     Creates an MD5 provider factory for the MD5 plugin
	/// </summary>
	IMD5ProviderFactory GetMD5ProviderFactory();
}
