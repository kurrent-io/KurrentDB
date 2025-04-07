// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;

namespace EventStore.Core.XUnit.Tests.Helpers;

public class FakeDisposable : IDisposable {
	private readonly Action _disposeAction;

	public FakeDisposable(Action disposeAction) {
		_disposeAction = disposeAction;
	}

	public void Dispose() => _disposeAction?.Invoke();
}
