// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using EventStore.Plugins;
using EventStore.Plugins.MD5;

namespace EventStore.Core.Hashing;

public class NetMD5Provider : Plugin, IMD5Provider {
	public HashAlgorithm Create() => System.Security.Cryptography.MD5.Create();
}
