// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.XUnit.Tests.Helpers;

public class FakeDisposable: IDisposable {
	private readonly Action _disposeAction;

	public FakeDisposable(Action disposeAction) {
		_disposeAction = disposeAction;
	}

	public void Dispose() => _disposeAction?.Invoke();
}
