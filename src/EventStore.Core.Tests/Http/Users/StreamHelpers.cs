// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.IO;
using EventStore.Common.Utils;

namespace EventStore.Core.Tests.Http.Users;

public static class StreamHelpers {
	public static void WriteJson<T>(this Stream stream, T data) {
		var bytes = data.ToJsonBytes();
		stream.Write(bytes, 0, bytes.Length);
	}
}
