// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Settings;
using NUnit.Framework;

namespace EventStore.Core.Tests.Settings;

[TestFixture]
public class ReaderThreadCountCalculatorTests {
	[TestCase]
	public void configured_takes_precedence() => Test(configuredCount: 1, processorCount: 4, isRunningInContainer: false, expected: 1);

	[TestCase]
	public void enforces_minimum() => Test(configuredCount: 0, processorCount: 1, isRunningInContainer: false, expected: 4);

	[TestCase]
	public void enforces_maximum() => Test(configuredCount: 0, processorCount: 20, isRunningInContainer: false, expected: 16);

	[TestCase]
	public void at_3_cores() => Test(configuredCount: 0, processorCount: 3, isRunningInContainer: false, expected: 6);

	[TestCase]
	public void at_4_cores() => Test(configuredCount: 0, processorCount: 4, isRunningInContainer: false, expected: 8);

	[TestCase]
	public void at_6_cores() => Test(configuredCount: 0, processorCount: 6, isRunningInContainer: false, expected: 12);

	[TestCase]
	public void running_in_docker_container_at_6_cores() => Test(configuredCount: 0, processorCount: 6, isRunningInContainer: true, expected: 4);

	[TestCase]
	public void running_in_docker_container_configured_takes_precedence() => Test(configuredCount: 1, processorCount: 4, isRunningInContainer: true, expected: 1);

	public static void Test(int configuredCount, int processorCount, bool isRunningInContainer, int expected) =>
		Assert.AreEqual(expected, ThreadCountCalculator.CalculateReaderThreadCount(configuredCount, processorCount, isRunningInContainer));
}
