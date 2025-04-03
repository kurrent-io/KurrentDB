// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Globalization;

namespace EventStore.Common.Utils;

public static class BytesFormatter {
	private static readonly string[] SizeOrders = new[] {"B", "KiB", "MiB", "GiB", "TiB"};
	private static readonly string[] SpeedOrders = new[] {"B/s", "KiB/s", "MiB/s", "GiB/s", "TiB/s"};
	private static readonly string[] NumberOrders = new[] {"", "K", "M", "G", "T"};

	public static string ToFriendlySpeedString(this double bytes) {
		return FormatSpeed((float)bytes);
	}

	public static string ToFriendlySizeString(this ulong bytes) {
		return bytes > long.MaxValue ? "more than long.MaxValue" : ToFriendlySizeString((long)bytes);
	}

	public static string ToFriendlySizeString(this long bytes) {
		return FormatLong(bytes, SizeOrders);
	}

	public static string ToFriendlyNumberString(this ulong number) {
		return number > long.MaxValue ? "more than long.MaxValue" : ToFriendlyNumberString((long)number);
	}

	public static string ToFriendlyNumberString(this long number) {
		return FormatLong(number, NumberOrders);
	}

	private static string FormatLong(long bytes, IEnumerable<string> orders) {
		const int scale = 1024;
		bool isNegative = bytes < 0;
		bytes = Math.Abs(bytes);

		long max = 1;
		string finalOrder = string.Empty;

		foreach (var order in orders) {
			max *= scale;
			finalOrder = order;
			if (bytes < max)
				break;
		}

		max /= scale;

		var friendlyStr = string.Format(CultureInfo.InvariantCulture,
			"{0}{1:##0.##}{2}",
			isNegative ? "-" : string.Empty,
			bytes * 1.0 / (double)max,
			finalOrder);
		return friendlyStr;
	}

	//the only difference is double vs long
	private static string FormatSpeed(double bytesPerSec) {
		const int scale = 1024;
		bool isNegative = bytesPerSec < 0; // verrry strange, but we need to show this if it happened already
		bytesPerSec = Math.Abs(bytesPerSec);

		long max = 1;
		string finalOrder = string.Empty;

		foreach (var speedOrder in SpeedOrders) {
			max *= scale;
			finalOrder = speedOrder;
			if (bytesPerSec < max)
				break;
		}

		max /= scale;

		var friendlyStr = string.Format(CultureInfo.InvariantCulture,
			"{0}{1:##0.##}{2}",
			isNegative ? "-" : string.Empty,
			bytesPerSec / (double)max,
			finalOrder);
		return friendlyStr;
	}
}
