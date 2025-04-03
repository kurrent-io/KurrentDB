// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;

namespace EventStore.Projections.Core.Utils;

public static class EncodingExtensions {
	public static string FromUtf8(this byte[] self) {
		return Helper.UTF8NoBom.GetString(self);
	}

	public static string FromUtf8(this ReadOnlyMemory<byte> self) {
		return Helper.UTF8NoBom.GetString(self.Span);
	}

	public static byte[] ToUtf8(this string self) {
		return Helper.UTF8NoBom.GetBytes(self);
	}

	public static string Apply(this string format, params object[] args) {
		return string.Format(format, args);
	}
}
