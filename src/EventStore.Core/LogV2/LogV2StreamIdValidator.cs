// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Common.Utils;

namespace EventStore.Core.LogV2;

public class LogV2StreamIdValidator : IValidator<string> {
	public LogV2StreamIdValidator() {
	}

	public void Validate(string streamId) {
		Ensure.NotNullOrEmpty(streamId, "streamId");
	}
}
