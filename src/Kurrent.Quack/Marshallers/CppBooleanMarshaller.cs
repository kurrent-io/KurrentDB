// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.Marshalling;

namespace Kurrent.Quack.Marshallers;

[CustomMarshaller(typeof(bool), MarshalMode.ManagedToUnmanagedIn, typeof(CppBooleanMarshaller))]
internal static class CppBooleanMarshaller {
	public static byte ConvertToUnmanaged(bool value) => Unsafe.BitCast<bool, byte>(value);
}
