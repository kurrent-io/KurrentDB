// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Reflection;
using Google.Protobuf.Reflection;

namespace KurrentDB.Api.Infrastructure.Protobuf;

public readonly ref partial struct ErrorEnums {
	public static EnumValueDescriptor GetEnumValueDescriptor<TEnum, TEnumDef>(TEnum value)
		where TEnum : struct, Enum
		where TEnumDef : IErrorEnum<TEnum>, allows ref struct {
		return value.GetCustomAttribute<TEnum, OriginalNameAttribute>()?.Name is { Length: > 0 } originalName
		       && TEnumDef.Descriptor.FindValueByName(originalName) is { } descriptor
			? descriptor
			: throw new KeyNotFoundException($"'{value}' is not a valid value of the protobuf enum '{typeof(TEnum).FullName}'.");
	}
}
