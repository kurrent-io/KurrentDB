// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using EventStore.Plugins.MD5;

namespace KurrentDB.Core.Hashing;

public class MD5 {
	private static readonly IMD5Provider _provider = new NetMD5Provider();

	public static HashAlgorithm Create() => _provider.Create();
}
