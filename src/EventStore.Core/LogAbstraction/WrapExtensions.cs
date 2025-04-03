// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.LogAbstraction;

public static class WrapExtensions {
	public static U Wrap<T, U>(this T t, Func<T, U> wrap) => wrap(t);
}
