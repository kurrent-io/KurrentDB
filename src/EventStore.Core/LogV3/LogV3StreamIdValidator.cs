// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Common.Utils;
using StreamId = System.UInt32;

namespace EventStore.Core.LogV3;

public class LogV3StreamIdValidator : IValidator<StreamId> {
	public LogV3StreamIdValidator() {
	}

	// Corresponding to LogV2StreamIdValidator we just check here that the steamId is
	// in the right space. The stream does not have to exist.
	public void Validate(StreamId streamId) {
		Ensure.Nonnegative(streamId, "streamId");
	}
}
