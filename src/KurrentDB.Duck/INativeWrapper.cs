// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace KurrentDB.Duck;

internal interface INativeWrapper<TSelf> : IDisposable
	where TSelf : struct, INativeWrapper<TSelf> {
	static abstract nint GetErrorString(nint handle);

	[StackTraceHidden]
	protected static void VerifyState([DoesNotReturnIf(true)] bool isError, nint handle) {
		if (isError)
			throw Interop.CreateException(TSelf.GetErrorString(handle));
	}
}
