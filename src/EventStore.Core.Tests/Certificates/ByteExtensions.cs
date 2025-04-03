// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Tests.Certificates;

public static class ByteExtensions {
	public static string PEM(this byte[] bytes, string label) {
		return $"-----BEGIN {label}-----\n" +  Convert.ToBase64String(bytes) + "\n" + $"-----END {label}-----\n";
	}
}
