// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;

namespace KurrentDB.Common.Utils;

public static class Helper {
	public static readonly UTF8Encoding UTF8NoBom = new(encoderShouldEmitUTF8Identifier: false);

	public static byte[] AsUTF8Bytes(this string str) => UTF8NoBom.GetBytes(str);

	public static void EatException(Action action) {
		try {
			action();
		} catch (Exception) {
		}
	}

	public static void EatException<TArg>(TArg arg, Action<TArg> action) {
		try {
			action(arg);
		} catch (Exception) {
		}
	}

	public static T EatException<T>(Func<T> action, T defaultValue = default) {
		try {
			return action();
		} catch (Exception) {
			return defaultValue;
		}
	}

	public static string FormatBinaryDump(byte[] logBulk) {
		return FormatBinaryDump(new ArraySegment<byte>(logBulk ?? Empty.ByteArray));
	}

	public static string FormatBinaryDump(ArraySegment<byte> logBulk) {
		if (logBulk.Count == 0)
			return "--- NO DATA ---";

		var sb = new StringBuilder();
		int cur = 0;
		int len = logBulk.Count;
		for (int row = 0, rows = (logBulk.Count + 15) / 16; row < rows; ++row) {
			sb.Append($"{row * 16:000000}:");
			for (int i = 0; i < 16; ++i, ++cur) {
				if (cur >= len)
					sb.Append("   ");
				else
					sb.Append($" {logBulk.Array[logBulk.Offset + cur]:X2}");
			}

			sb.Append("  | ");
			cur -= 16;
			for (int i = 0; i < 16; ++i, ++cur) {
				if (cur < len) {
					var b = (char)logBulk.Array[logBulk.Offset + cur];
					sb.Append(char.IsControl(b) ? '.' : b);
				}
			}

			sb.AppendLine();
		}

		return sb.ToString();
	}
}
