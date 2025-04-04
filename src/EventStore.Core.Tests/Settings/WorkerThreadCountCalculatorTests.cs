// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Settings;
using NUnit.Framework;

namespace EventStore.Core.Tests.Settings;

[TestFixture]
public class WorkerThreadCountCalculatorTests {
	[TestCase]
	public void configured_takes_precedence() => Test(configuredCount: 1, readerCount: 10, isRunningInContainer: false, expected: 1);

	[TestCase]
	public void enforces_minimum() => Test(configuredCount: 0, readerCount: 1, isRunningInContainer: false, expected: 5);

	[TestCase]
	public void enforces_maximum() => Test(configuredCount: 0, readerCount: 1000, isRunningInContainer: false, expected: 10);

	[TestCase]
	public void at_4_reader_threads() => Test(configuredCount: 0, readerCount: 4, isRunningInContainer: false, expected: 5);

	[TestCase]
	public void at_increased_reader_threads() => Test(configuredCount: 0, readerCount: 8, isRunningInContainer: false, expected: 10);

	[TestCase]
	public void running_in_docker_container_configured_takes_precedence() => Test(configuredCount: 1, readerCount: 4, isRunningInContainer: true, expected: 1);

	[TestCase]
	public void running_in_docker_container_at_4_reader_threads() => Test(configuredCount: 0, readerCount: 4, isRunningInContainer: true, expected: 5);

	public static void Test(int configuredCount, int readerCount, bool isRunningInContainer, int expected) =>
		Assert.AreEqual(expected, ThreadCountCalculator.CalculateWorkerThreadCount(configuredCount, readerCount, isRunningInContainer));

}
