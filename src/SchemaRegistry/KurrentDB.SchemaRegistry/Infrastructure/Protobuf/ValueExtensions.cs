// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace
// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Google.Protobuf.WellKnownTypes;
using Enum = System.Enum;

namespace Kurrent.Protobuf;

public static class ValueExtensions {
	// Epsilon tolerance for floating point comparisons to handle precision issues
	const double IntegerEpsilon = 1e-10;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryGetString(this Value value, [NotNullWhen(true)] out string? result) {
		if (value.KindCase == Value.KindOneofCase.StringValue) {
			result = value.StringValue;
			return true;
		}

		result = null;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryGetBoolean(this Value value, out bool result) {
		switch (value.KindCase) {
			case Value.KindOneofCase.BoolValue:
				result = value.BoolValue;
				return true;
			case Value.KindOneofCase.StringValue when bool.TryParse(value.StringValue, out result):
				return true;
			default:
				result = false;
				return false;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryGetDouble(this Value value, out double result) {
		switch (value.KindCase) {
			case Value.KindOneofCase.NumberValue:
				result = value.NumberValue;
				return true;
			case Value.KindOneofCase.StringValue when double.TryParse(value.StringValue, out result):
				return true;
			default:
				result = 0;
				return false;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryGetInt32(this Value value, out int result) {
		switch (value.KindCase) {
			case Value.KindOneofCase.NumberValue: {
				var d = value.NumberValue;
				if (d is >= int.MinValue and <= int.MaxValue) {
					var truncated = Math.Truncate(d);
					if (Math.Abs(d - truncated) < IntegerEpsilon) {
						result = (int)truncated;
						return true;
					}
				}

				result = 0;
				return false;
			}
			case Value.KindOneofCase.StringValue when int.TryParse(value.StringValue, out result):
				return true;
			default:
				result = 0;
				return false;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryGetInt64(this Value value, out long result) {
		switch (value.KindCase) {
			case Value.KindOneofCase.NumberValue: {
				var d = value.NumberValue;
				if (d is >= long.MinValue and <= long.MaxValue) {
					var truncated = Math.Truncate(d);
					if (Math.Abs(d - truncated) < IntegerEpsilon) {
						result = (long)truncated;
						return true;
					}
				}

				result = 0;
				return false;
			}
			case Value.KindOneofCase.StringValue when long.TryParse(value.StringValue, out result):
				return true;
			default:
				result = 0;
				return false;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryGetSingle(this Value value, out float result) {
		switch (value.KindCase) {
			case Value.KindOneofCase.NumberValue: {
				var d = value.NumberValue;
				if (d is >= float.MinValue and <= float.MaxValue) {
					result = (float)d;
					return true;
				}

				result = 0;
				return false;
			}
			case Value.KindOneofCase.StringValue when float.TryParse(value.StringValue, out result):
				return true;
			default:
				result = 0;
				return false;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryGetDecimal(this Value value, out decimal result) {
		switch (value.KindCase) {
			case Value.KindOneofCase.NumberValue: {
				var d = value.NumberValue;
				if (d is >= (double)decimal.MinValue and <= (double)decimal.MaxValue) {
					result = (decimal)d;
					return true;
				}

				result = 0;
				return false;
			}
			case Value.KindOneofCase.StringValue when decimal.TryParse(value.StringValue, out result):
				return true;
			default:
				result = 0;
				return false;
		}
	}

	public static bool TryGetGuid(this Value value, out Guid result) {
		if (value.KindCase == Value.KindOneofCase.StringValue)
			return Guid.TryParse(value.StringValue, out result);

		result = Guid.Empty;
		return false;
	}

	public static bool TryGetDateTime(this Value value, out DateTime result) {
		if (value.KindCase == Value.KindOneofCase.StringValue)
			return DateTime.TryParse(value.StringValue, out result);

		result = default;
		return false;
	}

	public static bool TryGetDateTimeOffset(this Value value, out DateTimeOffset result) {
		if (value.KindCase == Value.KindOneofCase.StringValue)
			return DateTimeOffset.TryParse(value.StringValue, out result);

		result = default;
		return false;
	}

	public static bool TryGetTimeSpan(this Value value, out TimeSpan result) {
		if (value.KindCase == Value.KindOneofCase.StringValue) {
			// Try standard TimeSpan parsing first
			if (TimeSpan.TryParse(value.StringValue, out result))
				return true;

			// Try ISO 8601 duration format (PT1H30M, P1DT2H, etc.)
			if (value.StringValue.Length > 1 && value.StringValue[0] == 'P') {
				try {
					result = System.Xml.XmlConvert.ToTimeSpan(value.StringValue);
					return true;
				} catch {
					// Fall through to false
				}
			}
		}

		result = TimeSpan.Zero;
		return false;
	}

	public static bool TryGetEnum<T>(this Value value, out T result) where T : struct, Enum {
		if (value.KindCase == Value.KindOneofCase.StringValue)
			return Enum.TryParse(value.StringValue, ignoreCase: true, out result);

		result = default;
		return false;
	}

	static string GetDisplayValue(Value value) => value.KindCase switch {
		Value.KindOneofCase.StringValue => $"'{value.StringValue}'",
		Value.KindOneofCase.NumberValue => value.NumberValue.ToString(CultureInfo.InvariantCulture),
		Value.KindOneofCase.BoolValue   => value.BoolValue.ToString(),
		Value.KindOneofCase.NullValue   => "null",
		Value.KindOneofCase.None        => "none",
		_                               => $"<{value.KindCase}>"
	};

	public static string GetString(this Value value) {
		return TryGetString(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to String");
	}

	public static bool GetBoolean(this Value value) {
		return TryGetBoolean(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to Boolean");
	}

	public static double GetDouble(this Value value) {
		return TryGetDouble(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to Double");
	}

	public static int GetInt32(this Value value) {
		return TryGetInt32(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to Int32");
	}

	public static long GetInt64(this Value value) {
		return TryGetInt64(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to Int64");
	}

	public static float GetSingle(this Value value) {
		return TryGetSingle(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to Single");
	}

	public static decimal GetDecimal(this Value value) {
		return TryGetDecimal(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to Decimal");
	}

	public static Guid GetGuid(this Value value) {
		return TryGetGuid(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to Guid");
	}

	public static DateTime GetDateTime(this Value value) {
		return TryGetDateTime(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to DateTime");
	}

	public static DateTimeOffset GetDateTimeOffset(this Value value) {
		return TryGetDateTimeOffset(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to DateTimeOffset");
	}

	public static TimeSpan GetTimeSpan(this Value value) {
		return TryGetTimeSpan(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to TimeSpan");
	}

	public static T GetEnum<T>(this Value value) where T : struct, Enum {
		return TryGetEnum<T>(value, out var result)
			? result
			: throw new InvalidOperationException($"Cannot convert Value of type {value.KindCase} with value {GetDisplayValue(value)} to {typeof(T).Name}");
	}
}
