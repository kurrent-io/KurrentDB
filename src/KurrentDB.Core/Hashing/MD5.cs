// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;

namespace KurrentDB.Core.Hashing;

public class MD5 {
	public static HashAlgorithm Create() => System.Security.Cryptography.MD5.Create();
}
