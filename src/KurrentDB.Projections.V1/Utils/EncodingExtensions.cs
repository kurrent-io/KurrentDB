// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;

namespace KurrentDB.Projections.Core.Utils;

public static class EncodingExtensions {
	public static string FromUtf8(this ReadOnlyMemory<byte> self) {
		return Helper.UTF8NoBom.GetString(self.Span);
	}
}
