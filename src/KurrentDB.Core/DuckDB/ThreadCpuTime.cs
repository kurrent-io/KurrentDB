// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;

namespace KurrentDB.Core.DuckDB;

// Reads the cumulative CPU time consumed by the calling thread.
// A delta between two readings is only meaningful when both are taken on the same thread.
internal static class ThreadCpuTime {
	// CLOCK_THREAD_CPUTIME_ID differs per libc: bits/time.h on Linux, sys/_types/_clockid_t on macOS
	private const int ClockThreadCpuTimeIdLinux = 3;
	private const int ClockThreadCpuTimeIdMacOS = 16;

	public static readonly bool IsSupported = Detect();

	public static long CurrentNanoseconds => OperatingSystem.IsWindows()
		? GetWindowsThreadCpuNanoseconds()
		: GetPosixThreadCpuNanoseconds();

	private static long GetPosixThreadCpuNanoseconds() {
		var clockId = OperatingSystem.IsLinux() ? ClockThreadCpuTimeIdLinux : ClockThreadCpuTimeIdMacOS;
		return clock_gettime(clockId, out var ts) == 0
			? ts.Seconds * 1_000_000_000L + ts.Nanoseconds
			: 0;
	}

	private static long GetWindowsThreadCpuNanoseconds() {
		// GetThreadTimes reports in 100ns units, accrued at scheduler-quantum granularity.
		// Cumulative totals are statistically accurate; very short individual deltas are not.
		return GetThreadTimes(GetCurrentThread(), out _, out _, out var kernelTime, out var userTime)
			? (kernelTime + userTime) * 100
			: 0;
	}

	private static bool Detect() {
		if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
			return false;

		try {
			_ = CurrentNanoseconds;
			return true;
		} catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) {
			return false;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Timespec {
		public nint Seconds;
		public nint Nanoseconds;
	}

	[DllImport("libc")]
	private static extern int clock_gettime(int clockId, out Timespec ts);

	[DllImport("kernel32.dll", ExactSpelling = true)]
	private static extern nint GetCurrentThread();

	[DllImport("kernel32.dll", ExactSpelling = true)]
	private static extern bool GetThreadTimes(nint thread, out long creationTime, out long exitTime, out long kernelTime, out long userTime);
}
