// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Common.Utils;

public static class Empty {
	public static byte[] ByteArray => [];

	public static readonly object Result = new();
	public static readonly string Xml = string.Empty;
	public static readonly string Json = "{}";
}
