// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;

namespace EventStore.Core.Services.Transport.Common;

public readonly struct HashCode {
	private readonly int _value;

	private HashCode(int value) {
		_value = value;
	}

	public static readonly HashCode Hash = default;

	public HashCode Combine<T>(T? value) where T : struct => Combine(value ?? default);

	public HashCode Combine<T>(T value) where T: struct {
		unchecked {
			return new((_value * 397) ^ value.GetHashCode());
		}
	}

	public HashCode Combine(string value){
		unchecked {
			return new((_value * 397) ^ (value?.GetHashCode() ?? 0));
		}
	}

	public static HashCode Combine<T>(IEnumerable<T> values) where T: struct =>
		values.Aggregate(Hash, (previous, value) => previous.Combine(value));

	public static HashCode Combine(IEnumerable<string> values) =>
		values.Aggregate(Hash, (previous, value) => previous.Combine(value));

	public static implicit operator int(HashCode value) => value._value;
}
