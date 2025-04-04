// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using EventStore.Common.Configuration;
using EventStore.Core.Metrics;
using Xunit;
using Xunit.Sdk;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class ProcessMetricsTests : IDisposable {
	private readonly TestMeterListener<int> _intListener;
	private readonly TestMeterListener<double> _doubleListener;
	private readonly TestMeterListener<long> _longListener;
	private readonly ProcessMetrics _sut;

	public ProcessMetricsTests() {
		var meter = new Meter($"{typeof(ProcessMetricsTests)}");
		_intListener = new TestMeterListener<int>(meter);
		_doubleListener = new TestMeterListener<double>(meter);
		_longListener = new TestMeterListener<long>(meter);

		var config = new Dictionary<MetricsConfiguration.ProcessTracker, bool>();

		foreach (var value in Enum.GetValues<MetricsConfiguration.ProcessTracker>()) {
			config[value] = true;
		}

		_sut = new ProcessMetrics(meter, TimeSpan.FromSeconds(42), scrapingPeriodInSeconds: 15, config, legacyNames: false);
		_sut.CreateObservableMetrics(new() {
			{ MetricsConfiguration.ProcessTracker.UpTime, "eventstore-proc-up-time" },
			{ MetricsConfiguration.ProcessTracker.Cpu, "eventstore-proc-cpu" },
			{ MetricsConfiguration.ProcessTracker.ThreadCount, "eventstore-proc-thread-count" },
			{ MetricsConfiguration.ProcessTracker.ThreadPoolPendingWorkItemCount, "eventstore-proc-thread-pool-pending-work-item-count" },
			{ MetricsConfiguration.ProcessTracker.LockContentionCount, "eventstore-proc-contention-count" },
			{ MetricsConfiguration.ProcessTracker.ExceptionCount, "eventstore-proc-exception-count" },
			{ MetricsConfiguration.ProcessTracker.TimeInGc, "eventstore-gc-time-in-gc" },
			{ MetricsConfiguration.ProcessTracker.HeapSize, "eventstore-gc-heap-size" },
			{ MetricsConfiguration.ProcessTracker.HeapFragmentation, "eventstore-gc-heap-fragmentation" },
			{ MetricsConfiguration.ProcessTracker.TotalAllocatedBytes, "eventstore-gc-total-allocated" },
			{ MetricsConfiguration.ProcessTracker.GcPauseDuration, "eventstore-gc-pause-duration" },
		});

		_sut.CreateMemoryMetric("eventstore-proc-mem", new() {
			{ MetricsConfiguration.ProcessTracker.MemWorkingSet, "working-set" },
			{ MetricsConfiguration.ProcessTracker.MemPagedBytes, "paged-bytes" },
			{ MetricsConfiguration.ProcessTracker.MemVirtualBytes, "virtual-bytes" },
		});

		_sut.CreateGcGenerationSizeMetric("eventstore-gc-generation-size", new() {
			{ MetricsConfiguration.ProcessTracker.Gen0Size, "gen0" },
			{ MetricsConfiguration.ProcessTracker.Gen1Size, "gen1" },
			{ MetricsConfiguration.ProcessTracker.Gen2Size, "gen2" },
			{ MetricsConfiguration.ProcessTracker.LohSize, "loh" },
		});

		_sut.CreateGcCollectionCountMetric("eventstore-gc-collection-count", new() {
			{ MetricsConfiguration.ProcessTracker.Gen0CollectionCount, "gen0" },
			{ MetricsConfiguration.ProcessTracker.Gen1CollectionCount, "gen1" },
			{ MetricsConfiguration.ProcessTracker.Gen2CollectionCount, "gen2" },
		});

		_sut.CreateDiskBytesMetric("eventstore-disk-io", new() {
			{ MetricsConfiguration.ProcessTracker.DiskReadBytes, "read" },
			{ MetricsConfiguration.ProcessTracker.DiskWrittenBytes, "written" },
		});

		_sut.CreateDiskOpsMetric("eventstore-disk-io", new() {
			{ MetricsConfiguration.ProcessTracker.DiskReadBytes, "read" },
			{ MetricsConfiguration.ProcessTracker.DiskWrittenBytes, "written" },
		});

		// To trigger the GC pause detection metric.
		GC.Collect();

		_intListener.Observe();
		_doubleListener.Observe();
		_longListener.Observe();
	}

	public void Dispose() {
		_intListener.Dispose();
		_doubleListener.Dispose();
		_longListener.Dispose();
	}

	[Fact]
	public void can_collect_proc_up_time() {
		Assert.Collection(
			_doubleListener.RetrieveMeasurements("eventstore-proc-up-time-seconds"),
			m => {
				Assert.True(m.Value > 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("pid", tag.Key);
						Assert.NotNull(tag.Value);
					});
			});
	}

	[Fact]
	public void can_collect_proc_cpu() {
		Assert.Collection(
			_doubleListener.RetrieveMeasurements("eventstore-proc-cpu"),
			m => {
				Assert.True(m.Value >= 0);
				Assert.Empty(m.Tags);
			});
	}

	[Fact]
	public void can_collect_proc_contention_count() {
		Assert.Collection(
			_longListener.RetrieveMeasurements("eventstore-proc-contention-count"),
			m => {
				Assert.True(m.Value >= 0);
				Assert.Empty(m.Tags);
			});
	}

	[Fact]
	public void can_collect_thread_pool_pending_work_item_count() {
		Assert.Collection(
			_longListener.RetrieveMeasurements("eventstore-proc-thread-pool-pending-work-item-count"),
			m => {
				Assert.True(m.Value >= 0);
				Assert.Empty(m.Tags);
			});
	}

	[Fact]
	public void can_collect_proc_exception_count() {
		Assert.Collection(
			_intListener.RetrieveMeasurements("eventstore-proc-exception-count"),
			m => {
				Assert.True(m.Value >= 0);
				Assert.Empty(m.Tags);
			});
	}

	[Fact]
	public void can_collect_gc_total_allocated() {
		Assert.Collection(
			_longListener.RetrieveMeasurements("eventstore-gc-total-allocated-bytes"),
			m => {
				Assert.True(m.Value > 0);
				Assert.Empty(m.Tags);
			});
	}

	[Fact]
	public void can_collect_thread_count() {
		Assert.Collection(
			_intListener.RetrieveMeasurements("eventstore-proc-thread-count"),
			m => {
				Assert.True(m.Value > 0);
				Assert.Empty(m.Tags);
			});
	}

	[Fact]
	public void can_collect_gc_time_in_gc() {
		Assert.Collection(
			_intListener.RetrieveMeasurements("eventstore-gc-time-in-gc"),
			m => {
				Assert.True(m.Value >= 0);
				Assert.Empty(m.Tags);
			});
	}

	[Fact]
	public void can_collect_gc_heap_size() {
		Assert.Collection(
			_longListener.RetrieveMeasurements("eventstore-gc-heap-size-bytes"),
			m => {
				Assert.True(m.Value > 0);
				Assert.Empty(m.Tags);
			});
	}

	[Fact]
	public void can_collect_gc_heap_fragmentation() {
		Assert.Collection(
			_doubleListener.RetrieveMeasurements("eventstore-gc-heap-fragmentation"),
			m => {
				Assert.True(m.Value > 0);
				Assert.Empty(m.Tags);
			});
	}

	[Fact]
	public void can_collect_proc_mem() {
		Assert.Collection(
			_longListener.RetrieveMeasurements("eventstore-proc-mem-bytes"),
			m => {
				Assert.True(m.Value > 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("kind", tag.Key);
						Assert.Equal("working-set", tag.Value);
					});
			},
			m => {
				Assert.True(m.Value >= 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("kind", tag.Key);
						Assert.Equal("paged-bytes", tag.Value);
					});
			},
			m => {
				Assert.True(m.Value >= 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("kind", tag.Key);
						Assert.Equal("virtual-bytes", tag.Value);
					});
			});
	}

	[Fact]
	public void can_collect_gc_generation_size() {
		Assert.Collection(
			_longListener.RetrieveMeasurements("eventstore-gc-generation-size-bytes"),
			m => {
				Assert.True(m.Value >= 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("generation", tag.Key);
						Assert.Equal("gen0", tag.Value);
					});
			},
			m => {
				Assert.True(m.Value > 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("generation", tag.Key);
						Assert.Equal("gen1", tag.Value);
					});
			},
			m => {
				Assert.True(m.Value > 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("generation", tag.Key);
						Assert.Equal("gen2", tag.Value);
					});
			},
			m => {
				Assert.True(m.Value > 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("generation", tag.Key);
						Assert.Equal("loh", tag.Value);
					});
			});
	}

	[Fact]
	public void can_collect_gc_collections_count() {
		Assert.Collection(
			_intListener.RetrieveMeasurements("eventstore-gc-collection-count"),
			m => {
				Assert.True(m.Value >= 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("generation", tag.Key);
						Assert.Equal("gen0", tag.Value);
					});
			},
			m => {
				Assert.True(m.Value >= 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("generation", tag.Key);
						Assert.Equal("gen1", tag.Value);
					});
			},
			m => {
				Assert.True(m.Value >= 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("generation", tag.Key);
						Assert.Equal("gen2", tag.Value);
					});
			});
	}

	[Fact]
	public void can_collect_disk_io_bytes() {
		Assert.Collection(
			_longListener.RetrieveMeasurements("eventstore-disk-io-bytes"),
			m => {
				Assert.True(m.Value >= 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("activity", tag.Key);
						Assert.Equal("read", tag.Value);
					});
			},
			m => {
				Assert.True(m.Value >= 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("activity", tag.Key);
						Assert.Equal("written", tag.Value);
					});
			});
	}

	[Fact]
	public void can_detect_gc_pauses() {
		for (var count = 0; count < 50; ++count) {
			try {
				Assert.Collection(
					_doubleListener.RetrieveMeasurements("eventstore-gc-pause-duration-seconds"),
					m => {
						Assert.Collection(
							m.Tags,
							tag => {
								Assert.Equal("range", tag.Key);
								Assert.Equal("16-20 seconds", tag.Value);
							});

						Assert.True(m.Value > 0);
					});

				return;
			} catch (CollectionException) {
			}

			Thread.Sleep(10);
			_doubleListener.Observe();
		}
	}
}
