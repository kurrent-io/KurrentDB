// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Time;

namespace EventStore.Core.Metrics;

// When observed, this returns the max value that it has been asked to record over the last x
// seconds, where x is not exact, but within a known range.
// The idea is, if we expect to take an observation every e.g. ~15 seconds, and that observation
// contains the max duration over the last 16-20 seconds, then every duration will be captured
// at least once (although, some of them will be captured twice, which isn't as bad as missing them).
//
// It is useful for observing values with high volatility that may spike up significantly and then
// return to normal between scrapes and therefore the spike would otherwise be missing from the metrics.
//
// It works by recording max values in buckets, each bucket representing one sub-period.
// The max value for the period is then the max across all the buckets.
// This keeps resource consumption low even when there are a lot of durations recorded.
// On recording/observing, buckets that contain data that is old enough to not be relevant are reset.
//
// Multiple threads can Record and Observe concurrently, but only one thread should record at a time
// or recordings may be dropped.
public class RecentMax<T> {
	private readonly Comparer<T> _comparer = Comparer<T>.Default;
	private readonly long _ticksPerBucket;
	private readonly int _numBuckets;
	private readonly T[] _maxBuckets;
	private readonly object _lock = new();

	// the sub period when this was last accessed (for recording or observation)
	// we use this to determine what data (buckets) have become stale in the mean time
	private long _lastSubPeriod;

	public RecentMax(int expectedScrapeIntervalSeconds) {
		var calc = new BucketCalculator(expectedScrapeIntervalSeconds);
		_numBuckets = calc.NumBuckets;
		MinPeriodSeconds = calc.MinPeriodSeconds;
		MaxPeriodSeconds = calc.MaxPeriodSeconds;

		_ticksPerBucket = calc.SecondsPerBucket * Instant.TicksPerSecond;

		_maxBuckets = new T[_numBuckets];
	}
	 
	public long MinPeriodSeconds { get; init; }
	public long MaxPeriodSeconds { get; init; }

	public Instant Record(Instant now, T value) {
		var currentSubPeriod = now.Ticks / _ticksPerBucket;

		ResetStaleBuckets(currentSubPeriod);

		var currentIndex = (int)(currentSubPeriod % _numBuckets);
		if (_comparer.Compare(value, _maxBuckets[currentIndex]) > 0)
			_maxBuckets[currentIndex] = value;

		return now;
	}

	public T Observe(Instant now) {
		ResetStaleBuckets(now.Ticks / _ticksPerBucket);
		return _maxBuckets.Max();
	}

	private void ResetStaleBuckets(long currentSubPeriod) {
		lock (_lock) {
			// reset from the _lastSubPeriod (exclusive) to the currentSubPeriod (inclusive)
			// which means we need to reset n buckets starting from the bucket after the
			// _lastSubPeriod where n is currentSubPeriod - _lastSubPeriod, but at most _numBuckets.
			var numBucketsToReset = Math.Min(currentSubPeriod - _lastSubPeriod, _numBuckets);
			var targetBucket = (_lastSubPeriod + 1) % _numBuckets;
			for (var n = 0; n < numBucketsToReset; n++) {
				_maxBuckets[targetBucket] = default;
				targetBucket = (targetBucket + 1) % _numBuckets;
			}

			_lastSubPeriod = currentSubPeriod;
		}
	}

	// calculates the number of buckets and the length of time each bucket represents. we calculate
	// these such that the amount of time that observations cover is more than the expected scrape
	// interval, so that we are likely to capture all the spikes even if a scape is a little late.
	// if a scrape is _really_ late, e.g. because a blocking GC pauses all the threads for a long
	// time, then some datapoints to be missed because they have become stale - but they will be
	// the datapoints immediately before the GC, not the spike from the GC itself.
	// we can't improve on this by comparing the time of the scrape with the time of the previous
	// scrape because there might be multiple scrapers.
	public class BucketCalculator {
		public BucketCalculator(int expectedScrapeIntervalSeconds) {
			if (expectedScrapeIntervalSeconds == 0) {
				// observed durations in the range 0-1s
				NumBuckets = 1;
				SecondsPerBucket = 1;
			} else if (expectedScrapeIntervalSeconds == 1) {
				// observed durations in the range 2-3s
				NumBuckets = 3;
				SecondsPerBucket = 1;
			} else if (expectedScrapeIntervalSeconds == 5) {
				// observed durations in the range 6-8s
				NumBuckets = 4;
				SecondsPerBucket = 2;
			} else if (expectedScrapeIntervalSeconds == 10) {
				// observed durations in the range 12-15s
				NumBuckets = 5;
				SecondsPerBucket = 3;
			} else if (expectedScrapeIntervalSeconds % 15 == 0) {
				NumBuckets = 5;
				SecondsPerBucket = expectedScrapeIntervalSeconds / 15 * 4;
			} else {
				throw new ArgumentException(
					$"ExpectedScrapeIntervalSeconds must be 0, 1, 5, 10 or a multiple of 15, " +
					$"but was {expectedScrapeIntervalSeconds}");
			}

			MaxPeriodSeconds = NumBuckets * SecondsPerBucket;
			MinPeriodSeconds = MaxPeriodSeconds - SecondsPerBucket;
		}

		public int NumBuckets { get; init; }
		public int SecondsPerBucket { get; init; }
		public int MinPeriodSeconds { get; init; }
		public int MaxPeriodSeconds { get; init; }
	}
}
