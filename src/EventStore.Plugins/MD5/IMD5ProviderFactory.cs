// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.MD5;

public interface IMD5ProviderFactory {
	/// <summary>
	///     Builds an MD5 provider for the MD5 plugin
	/// </summary>
	IMD5Provider Build();
}
