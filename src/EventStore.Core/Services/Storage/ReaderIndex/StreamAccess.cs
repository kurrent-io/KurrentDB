// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Services.Storage.ReaderIndex;

public struct StreamAccess {
	public readonly bool Granted;
	public readonly bool Public;

	public StreamAccess(bool granted) {
		Granted = granted;
		Public = false;
	}

	public StreamAccess(bool granted, bool @public) {
		Granted = granted;
		Public = @public;
	}
}
