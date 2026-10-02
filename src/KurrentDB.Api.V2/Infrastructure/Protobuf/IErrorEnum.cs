// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.Reflection;

namespace KurrentDB.Api.Infrastructure.Protobuf;

public interface IErrorEnum<T>
	where T : struct, Enum {
	public static abstract EnumDescriptor Descriptor { get; }

	public static abstract Type? GetDetailsType(T value);
}
