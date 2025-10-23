// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using Serilog;

namespace KurrentDB.Core.Settings;


public static class ThreadCountCalculator {
	private const int ReaderThreadCountFloor = 4;

	public static int CalculateReaderThreadCount(int configuredCount, int processorCount,
		bool isRunningInContainer) {
		if (configuredCount > 0) {
			Log.Information(
				"ReaderThreadsCount set to {readerThreadsCount:N0}. " +
				"Calculated based on processor count of {processorCount:N0} and configured value of {configuredCount:N0}",
				configuredCount,
				processorCount, configuredCount);
			return configuredCount;
		}

		if (isRunningInContainer) {
			Log.Information(
				"ReaderThreadsCount set to {readerThreadsCount:N0}. " +
				"Calculated based on containerized environment and configured value of {configuredCount:N0}",
				ContainerizedEnvironment.ReaderThreadCount,
				configuredCount);
			return ContainerizedEnvironment.ReaderThreadCount;
		}

		var readerCount = Math.Clamp(processorCount * 2, ReaderThreadCountFloor, 16);
		Log.Information(
			"ReaderThreadsCount set to {readerThreadsCount:N0}. " +
			"Calculated based on processor count of {processorCount:N0} and configured value of {configuredCount:N0}",
			readerCount,
			processorCount, configuredCount);
		return readerCount;
	}
}
