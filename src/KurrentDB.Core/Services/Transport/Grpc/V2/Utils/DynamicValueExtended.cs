// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault
// ReSharper disable CheckNamespace

#nullable enable

namespace KurrentDB.Protobuf;

public sealed partial class DynamicValue {
	public bool TryMapValue<T>(out T? value) {
		switch (KindCase) {
			case KindOneofCase.NullValue:
			case KindOneofCase.None:
				value = default;
				return true;

			case KindOneofCase.StringValue when StringValue is T typedValue:
				value = typedValue;
				return true;

			case KindOneofCase.BooleanValue when BooleanValue is T booleanValue:
				value = booleanValue;
				return true;

			case KindOneofCase.Int32Value when Int32Value is T int32Value:
				value = int32Value;
				return true;

			case KindOneofCase.Int64Value when Int64Value is T int64Value:
				value = int64Value;
				return true;

			case KindOneofCase.FloatValue when FloatValue is T floatValue:
				value = floatValue;
				return true;

			case KindOneofCase.DoubleValue when DoubleValue is T doubleValue:
				value = doubleValue;
				return true;

			case KindOneofCase.TimestampValue when TimestampValue.ToDateTime() is T dateTimeValue:
				value = dateTimeValue;
				return true;

			case KindOneofCase.DurationValue when DurationValue.ToTimeSpan() is T timeSpanValue:
				value = timeSpanValue;
				return true;

			case KindOneofCase.BytesValue when BytesValue.Memory is T memoryValue:
				value = memoryValue;
				return true;
		}

		value = default;
		return false;
	}
}
