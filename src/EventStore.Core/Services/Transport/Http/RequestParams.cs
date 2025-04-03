// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Services.Transport.Http;

public struct RequestParams(bool done, TimeSpan timeout) {
	public readonly bool IsDone = done;
	public readonly TimeSpan Timeout = timeout;

	public RequestParams(bool done) : this(done, TimeSpan.Zero) {
	}

	public RequestParams(TimeSpan timeout) : this(false, timeout) {
	}
}
