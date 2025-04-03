// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Helpers;

public class NoOpAction {
	public static Action Instance { get; } = () => { };
}

public class NoOpAction<T> {
	public static Action<T> Instance { get; } = _ => { };
}

public static class ActionExtensions {
	public static Action OrNoOp(this Action self) => self ?? NoOpAction.Instance;
	public static Action<T> OrNoOp<T>(this Action<T> self) => self ?? NoOpAction<T>.Instance;
}
