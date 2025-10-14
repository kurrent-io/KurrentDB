// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;

namespace EventStore.Plugins.MD5;

public interface IMD5Provider : IPlugableComponent {
	/// <summary>
	///     Creates an instance of the MD5 hash algorithm implementation
	/// </summary>
	HashAlgorithm Create();
}
