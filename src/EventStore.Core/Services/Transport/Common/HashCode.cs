// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;

namespace EventStore.Core.Services.Transport.Common;

public struct HashCode {
	private readonly int _value;

	private HashCode(int value) {
		_value = value;
	}

	public static readonly HashCode Hash = default;

	public readonly HashCode Combine<T>(T? value) where T : struct => Combine(value ?? default);

	public readonly HashCode Combine<T>(T value) where T : struct {
		unchecked {
			return new HashCode((_value * 397) ^ value.GetHashCode());
		}
	}

	public readonly HashCode Combine(string value) {
		unchecked {
			return new HashCode((_value * 397) ^ (value?.GetHashCode() ?? 0));
		}
	}

	public readonly HashCode Combine<T>(IEnumerable<T> values) where T : struct =>
		values.Aggregate(Hash, (previous, value) => previous.Combine(value));

	public readonly HashCode Combine(IEnumerable<string> values) =>
		values.Aggregate(Hash, (previous, value) => previous.Combine(value));

	public static implicit operator int(HashCode value) => value._value;
}
