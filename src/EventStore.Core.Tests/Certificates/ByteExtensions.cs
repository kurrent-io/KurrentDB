// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;

namespace EventStore.Core.Tests.Certificates;

public static class ByteExtensions {
	public static string PEM(this byte[] bytes, string label) {
		return $"-----BEGIN {label}-----\n" + Convert.ToBase64String(bytes) + "\n" + $"-----END {label}-----\n";
	}
}
