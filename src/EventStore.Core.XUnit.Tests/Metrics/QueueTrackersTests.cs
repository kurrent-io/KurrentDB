// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Metrics;
using EventStore.Core.Time;
using Xunit;
using Conf = EventStore.Common.Configuration.MetricsConfiguration;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class QueueTrackersTests {
	[Fact]
	public void default_trackers_yields_noop_tracker() {
		var sut = new QueueTrackers();
		var tracker = sut.GetTrackerForQueue("MainQueue");
		Assert.Equal("NoOp", tracker.Name);
	}

	[Fact]
	public void not_matched_yields_noop_tracker() {
		var sut = GenSut(
			new Conf.LabelMappingCase {
				Regex = "MainQueue",
				Label = "MainQueue",
			});


		var tracker = sut.GetTrackerForQueue("MainQueue");
		Assert.Equal("MainQueue", tracker.Name);

		tracker = sut.GetTrackerForQueue("WriterQueue");
		Assert.Equal("NoOp", tracker.Name);
	}

	[Fact]
	public void empty_tracker_yields_no_op() {
		var sut = GenSut(
			new Conf.LabelMappingCase {
				Regex = "MainQueue",
				Label = "",
			},
			new Conf.LabelMappingCase {
				Regex = "WriterQueue",
				Label = "WriterQueue",
			});


		var tracker = sut.GetTrackerForQueue("WriterQueue");
		Assert.Equal("WriterQueue", tracker.Name);

		tracker = sut.GetTrackerForQueue("MainQueue");
		Assert.Equal("NoOp", tracker.Name);
	}

	[Fact]
	public void unspecified_label_yields_no_op() {
		var sut = GenSut(
			new Conf.LabelMappingCase {
				Regex = "MainQueue",
				// no Label
			});

		var tracker = sut.GetTrackerForQueue("MainQueue");
		Assert.Equal("NoOp", tracker.Name);
	}

	[Fact]
	public void unspecified_regex_does_not_match() {
		var sut = GenSut(
			new Conf.LabelMappingCase {
				// no Regex
				Label = "MainQueue",
			});

		var tracker = sut.GetTrackerForQueue("MainQueue");
		Assert.Equal("NoOp", tracker.Name);
	}

	[Fact]
	public void patterns_applied_in_order() {
		var sut = GenSut(
			new Conf.LabelMappingCase {
				Regex = "MainQueue",
				Label = "MainQueue",
			},
			new Conf.LabelMappingCase {
				Regex = ".*",
				Label = "OtherQueue",
			});

		var tracker = sut.GetTrackerForQueue("MainQueue");
		Assert.Equal("MainQueue", tracker.Name);

		tracker = sut.GetTrackerForQueue("WriterQueue");
		Assert.Equal("OtherQueue", tracker.Name);
	}

	[Fact]
	public void matches_groups() {
		var sut = GenSut(
			new Conf.LabelMappingCase {
				Regex = "Worker #(.*)",
				Label = "Worker Queue $1",
			});

		var tracker = sut.GetTrackerForQueue("Worker #3");
		Assert.Equal("Worker Queue 3", tracker.Name);
	}

	QueueTrackers GenSut(params Conf.LabelMappingCase[] map) =>
		new(map,
			x => new FakeTracker { Name = x },
			x => new FakeTracker { Name = x },
			x => new FakeTracker { Name = x });

	class FakeTracker : IDurationMaxTracker, IQueueProcessingTracker, IQueueBusyTracker {
		public string Name { get; init; }

		public void EnterBusy() {
		}

		public void EnterIdle() {
		}

		public Instant RecordNow(Instant start) => start;

		public Instant RecordNow(Instant start, string messageType) => start;
	}
}
