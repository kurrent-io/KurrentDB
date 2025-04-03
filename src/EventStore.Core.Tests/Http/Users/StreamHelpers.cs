// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using EventStore.Common.Utils;
using EventStore.Core.Util;

namespace EventStore.Core.Tests.Http.Users;

public static class StreamHelpers {
	public static void WriteJson<T>(this Stream stream, T data) {
		var bytes = data.ToJsonBytes();
		stream.Write(bytes, 0, bytes.Length);
	}
}
