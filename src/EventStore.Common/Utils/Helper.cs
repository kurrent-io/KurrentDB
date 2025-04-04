// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace EventStore.Common.Utils;

public static class Helper {
	public static readonly UTF8Encoding UTF8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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

	public static T EatException<T>(Func<T> action, T defaultValue = default(T)) {
		try {
			return action();
		} catch (Exception) {
			return defaultValue;
		}
	}

	public static string GetDefaultLogsDir() {
		return Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "es-logs");
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
			sb.AppendFormat("{0:000000}:", row * 16);
			for (int i = 0; i < 16; ++i, ++cur) {
				if (cur >= len)
					sb.Append("   ");
				else
					sb.AppendFormat(" {0:X2}", logBulk.Array[logBulk.Offset + cur]);
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
