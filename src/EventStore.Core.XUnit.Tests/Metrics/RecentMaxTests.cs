// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Metrics;
using EventStore.Core.Time;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class RecentMaxTests {
	private readonly RecentMax<long> _sut;
	private readonly FakeClock _clock = new();

	public RecentMaxTests() {
		_sut = new RecentMax<long>(expectedScrapeIntervalSeconds: 15);
	}

	[Fact]
	public void record_now_returns_now() {
		_clock.SecondsSinceEpoch = 500;
		var now = Record(123);
		Assert.Equal(_clock.Now, now);
	}

	[Fact]
	public void no_records() {
		AssertObservedValue(0);
	}

	[Fact]
	public void one_record() {
		AssertObservedValue(0);
		Record(1);
		AssertObservedValue(1);
	}

	[Fact]
	public void two_records_instant() {
		AssertObservedValue(0);
		Record(1);
		AssertObservedValue(1);
		Record(2);
		AssertObservedValue(2);
	}

	[Fact]
	public void two_records_ascending() {
		AssertObservedValue(0);

		AdvanceSeconds(1);
		Record(1);
		AssertObservedValue(1);

		AdvanceSeconds(1);
		Record(2);
		AssertObservedValue(2);
	}

	[Fact]
	public void two_records_descending() {
		AssertObservedValue(0);

		AdvanceSeconds(1);
		Record(2);
		AssertObservedValue(2);

		AdvanceSeconds(1);
		Record(1);
		AssertObservedValue(2);
	}

	[Fact]
	public void removes_stale_data() {
		Record(1);

		AdvanceSeconds(19);
		AssertObservedValue(1);

		AdvanceSeconds(1);
		AssertObservedValue(0);
	}

	[Fact]
	public void removes_stale_data_incrementally() {
		// record a series of values, each 4s apart (so in a different bucket)
		// there are 5 buckets
		AssertObservedValue(0);

		Record(10);
		AssertObservedValue(10);

		AdvanceSeconds(4);
		Record(9);
		AssertObservedValue(10);

		AdvanceSeconds(4);
		Record(8);
		AssertObservedValue(10);

		AdvanceSeconds(4);
		Record(7);
		AssertObservedValue(10);

		AdvanceSeconds(4);
		Record(6);
		AssertObservedValue(10);

		AdvanceSeconds(4);
		Record(5);

		// original measurement has become stale
		AssertObservedValue(9);
	}

	private Instant Record(long value) =>
		_sut.Record(_clock.Now, value);

	private void AdvanceSeconds(long seconds) =>
		_clock.AdvanceSeconds(seconds);

	private void AssertObservedValue(long value) =>
		Assert.Equal(value, _sut.Observe(_clock.Now));

	public class BucketCalculatorTests {
		[Theory]
		[InlineData(0, 1, 1, 0, 1)]
		[InlineData(1, 3, 1, 2, 3)]
		[InlineData(5, 4, 2, 6, 8)]
		[InlineData(10, 5, 3, 12, 15)]
		[InlineData(15, 5, 4, 16, 20)]
		[InlineData(30, 5, 8, 32, 40)]
		[InlineData(45, 5, 12, 48, 60)]
		[InlineData(60, 5, 16, 64, 80)]
		[InlineData(75, 5, 20, 80, 100)]
		[InlineData(90, 5, 24, 96, 120)]
		[InlineData(105, 5, 28, 112, 140)]
		[InlineData(120, 5, 32, 128, 160)]
		public void calculates_happy_path(
			int scrapeIntervalSeconds,
			int expectedNumBuckets,
			int expectedSecondsPerBucket,
			int expectedMinPeriodSeconds,
			int expectedMaxPeriodSeconds) {

			var sut = new RecentMax<long>.BucketCalculator(scrapeIntervalSeconds);

			Assert.Equal(expectedNumBuckets, sut.NumBuckets);
			Assert.Equal(expectedSecondsPerBucket, sut.SecondsPerBucket);
			Assert.Equal(expectedMinPeriodSeconds, sut.MinPeriodSeconds);
			Assert.Equal(expectedMaxPeriodSeconds, sut.MaxPeriodSeconds);
		}

		[Fact]
		public void throws() {
			var ex = Assert.Throws<ArgumentException>(() => {
				_ = new RecentMax<long>.BucketCalculator(16);
			});

			Assert.Equal(
				"ExpectedScrapeIntervalSeconds must be 0, 1, 5, 10 or a multiple of 15, but was 16",
				ex.Message);
		}
	}
}
