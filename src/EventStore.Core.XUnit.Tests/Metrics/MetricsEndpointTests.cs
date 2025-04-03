// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using EventStore.Common.Configuration;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

[Collection("MetricsLabelTests")]
public class MetricsEndpointTests : DirectoryPerTest<MetricsEndpointTests> {
	[Fact]
	public async Task can_produce_kurrent_metrics() {
		var content = await Query(legacy: false);
		foreach (var expected in KurrentMetrics)
			Assert.Contains(expected, content);
	}

	[Fact]
	public async Task can_produce_legacy_metrics() {
		var content = await Query(legacy: true);
		foreach (var expected in EventStoreMetrics)
			Assert.Contains(expected, content);
	}

	async Task<string> Query(bool legacy) {
		var configuration = new ConfigurationBuilder()
			.AddSection($"{KurrentConfigurationKeys.Prefix}:Metrics", x => x
				.AddJsonFile("./Metrics/Conf/test-metrics-config.json")
				.AddInMemoryCollection([
					new("Meters:0", legacy
						? "EventStore.Core"
						: "KurrentDB.Core"),
					new("Meters:1", legacy
						? "EventStore.Projections.Core"
						: "KurrentDB.Projections.Core"),
				]))
			.Build();
		await using var sut = new MiniNode<LogFormat.V2, string>(Fixture.Directory, configuration: configuration);
		await sut.Start();
		sut.HttpClient.DefaultRequestHeaders.Add(
			"Accept",
			"application/openmetrics-text;version=1.0.0,application/openmetrics-text;version=0.0.1;q=0.75,text/plain;version=0.0.4;q=0.5,*/*;q=0.1");
		var result = await sut.HttpClient.GetAsync("/metrics");
		Assert.Equal(HttpStatusCode.OK, result.StatusCode);

		Assert.Equal(
			legacy
				? "text/plain; charset=utf-8; version=0.0.4"
				: "application/openmetrics-text; version=1.0.0; charset=utf-8",
			$"{result.Content.Headers.ContentType}");

		var content = await result.Content.ReadAsStringAsync();
		return content;
	}

	static IEnumerable<string> KurrentMetrics => [
		"# TYPE kurrentdb_cache_hits_misses counter",
		"# TYPE kurrentdb_cache_resources_entries gauge",
		"# TYPE kurrentdb_checkpoints gauge",
		"# TYPE kurrentdb_current_incoming_grpc_calls gauge",
		"# TYPE kurrentdb_disk_io_bytes counter",
		"# TYPE kurrentdb_disk_io_operations counter",
		"# TYPE kurrentdb_elections_count counter",
		"# TYPE kurrentdb_gc_allocated_bytes counter",
		"# TYPE kurrentdb_gc_collection_count counter",
		"# TYPE kurrentdb_gc_generation_size_bytes gauge",
		"# TYPE kurrentdb_gc_heap_fragmentation gauge",
		"# TYPE kurrentdb_gc_heap_size_bytes gauge",
		"# TYPE kurrentdb_gc_pause_duration_max_seconds gauge",
		"# TYPE kurrentdb_gc_time_in_gc gauge",
		"# TYPE kurrentdb_incoming_grpc_calls counter",
		"# TYPE kurrentdb_io_bytes counter",
		"# TYPE kurrentdb_io_events counter",
		"# TYPE kurrentdb_io_record_read_duration_seconds histogram",
		"# TYPE kurrentdb_logical_chunk_read_distribution histogram",
		"# TYPE kurrentdb_proc_contention_count counter",
		"# TYPE kurrentdb_proc_cpu gauge",
		"# TYPE kurrentdb_proc_exception_count counter",
		"# TYPE kurrentdb_proc_mem_bytes gauge",
		"# TYPE kurrentdb_proc_thread_count gauge",
		"# TYPE kurrentdb_proc_thread_pool_pending_work_item_count gauge",
		"# TYPE kurrentdb_proc_up_time_seconds counter",
		"# TYPE kurrentdb_queue_busy_seconds counter",
		"# TYPE kurrentdb_queue_processing_duration_seconds histogram",
		"# TYPE kurrentdb_queue_queueing_duration_max_seconds gauge",
		"# TYPE kurrentdb_statuses gauge",
		"# TYPE kurrentdb_sys_cpu gauge",
		"# TYPE kurrentdb_sys_disk_bytes gauge",
		"# TYPE kurrentdb_sys_mem_bytes gauge",
		"# TYPE kurrentdb_writer_flush_duration_max_seconds gauge",
		"# TYPE kurrentdb_writer_flush_size_max gauge",

		"# UNIT kurrentdb_cache_resources_entries entries",
		"# UNIT kurrentdb_disk_io_bytes bytes",
		"# UNIT kurrentdb_disk_io_operations operations",
		"# UNIT kurrentdb_gc_allocated_bytes bytes",
		"# UNIT kurrentdb_gc_generation_size_bytes bytes",
		"# UNIT kurrentdb_gc_heap_size_bytes bytes",
		"# UNIT kurrentdb_gc_pause_duration_max_seconds seconds",
		"# UNIT kurrentdb_io_bytes bytes",
		"# UNIT kurrentdb_io_events events",
		"# UNIT kurrentdb_io_record_read_duration_seconds seconds",
		"# UNIT kurrentdb_proc_mem_bytes bytes",
		"# UNIT kurrentdb_proc_up_time_seconds seconds",
		"# UNIT kurrentdb_queue_busy_seconds seconds",
		"# UNIT kurrentdb_queue_processing_duration_seconds seconds",
		"# UNIT kurrentdb_queue_queueing_duration_max_seconds seconds",
		"# UNIT kurrentdb_sys_disk_bytes bytes",
		"# UNIT kurrentdb_sys_mem_bytes bytes",
		"# UNIT kurrentdb_writer_flush_duration_max_seconds seconds",

		"kurrentdb_cache_hits_misses_total{",
		"kurrentdb_cache_resources_entries{",
		"kurrentdb_checkpoints{",
		"kurrentdb_current_incoming_grpc_calls{",
		"kurrentdb_disk_io_bytes_total{",
		"kurrentdb_disk_io_operations_total{",
		"kurrentdb_elections_count_total{",
		"kurrentdb_gc_allocated_bytes_total{",
		"kurrentdb_gc_collection_count_total{",
		"kurrentdb_gc_generation_size_bytes{",
		"kurrentdb_gc_heap_fragmentation{",
		"kurrentdb_gc_heap_size_bytes{",
		"kurrentdb_gc_pause_duration_max_seconds{",
		"kurrentdb_gc_time_in_gc{",
		"kurrentdb_incoming_grpc_calls_total{",
		"kurrentdb_io_bytes_total{",
		"kurrentdb_io_events_total{",
		"kurrentdb_io_record_read_duration_seconds_bucket{",
		"kurrentdb_io_record_read_duration_seconds_count{",
		"kurrentdb_io_record_read_duration_seconds_sum{",
		"kurrentdb_logical_chunk_read_distribution_bucket{",
		"kurrentdb_logical_chunk_read_distribution_count{",
		"kurrentdb_logical_chunk_read_distribution_sum{",
		"kurrentdb_proc_contention_count_total{",
		"kurrentdb_proc_cpu{",
		"kurrentdb_proc_exception_count_total{",
		"kurrentdb_proc_mem_bytes{",
		"kurrentdb_proc_thread_count{",
		"kurrentdb_proc_thread_pool_pending_work_item_count{",
		"kurrentdb_proc_up_time_seconds_total{",
		"kurrentdb_queue_busy_seconds_total{",
		"kurrentdb_queue_processing_duration_seconds_bucket{",
		"kurrentdb_queue_processing_duration_seconds_count{",
		"kurrentdb_queue_processing_duration_seconds_sum{",
		"kurrentdb_queue_queueing_duration_max_seconds{",
		"kurrentdb_statuses{",
		"kurrentdb_sys_cpu{",
		"kurrentdb_sys_disk_bytes{",
		"kurrentdb_sys_mem_bytes{",
		"kurrentdb_writer_flush_duration_max_seconds{",
		"kurrentdb_writer_flush_size_max{",
	];

	static IEnumerable<string> EventStoreMetrics => [
		"# TYPE eventstore_cache_hits_misses counter",
		"# TYPE eventstore_cache_resources_entries gauge",
		"# TYPE eventstore_checkpoints gauge",
		"# TYPE eventstore_current_incoming_grpc_calls gauge",
		"# TYPE eventstore_disk_io_bytes counter",
		"# TYPE eventstore_disk_io_operations counter",
		"# TYPE eventstore_elections_count counter",
		"# TYPE eventstore_gc_collection_count counter",
		"# TYPE eventstore_gc_generation_size_bytes gauge",
		"# TYPE eventstore_gc_heap_fragmentation gauge",
		"# TYPE eventstore_gc_heap_size_bytes gauge",
		"# TYPE eventstore_gc_pause_duration_max_seconds gauge",
		"# TYPE eventstore_gc_time_in_gc gauge",
		"# TYPE eventstore_gc_total_allocated counter",
		"# TYPE eventstore_incoming_grpc_calls counter",
		"# TYPE eventstore_io_bytes counter",
		"# TYPE eventstore_io_events counter",
		"# TYPE eventstore_logical_chunk_read_distribution histogram",
		"# TYPE eventstore_proc_contention_count counter",
		"# TYPE eventstore_proc_cpu gauge",
		"# TYPE eventstore_proc_exception_count counter",
		"# TYPE eventstore_proc_mem_bytes gauge",
		"# TYPE eventstore_proc_thread_count gauge",
		"# TYPE eventstore_proc_thread_pool_pending_work_item_count gauge",
		"# TYPE eventstore_proc_up_time counter",
		"# TYPE eventstore_queue_busy_seconds counter",
		"# TYPE eventstore_queue_queueing_duration_max_seconds gauge",
		"# TYPE eventstore_statuses counter",
		"# TYPE eventstore_sys_cpu gauge",
		"# TYPE eventstore_sys_disk_bytes gauge",
		"# TYPE eventstore_sys_mem_bytes gauge",
		"# TYPE eventstore_writer_flush_duration_max_seconds gauge",
		"# TYPE eventstore_writer_flush_size_max gauge",

		"eventstore_cache_hits_misses{",
		"eventstore_cache_resources_entries{",
		"eventstore_checkpoints{",
		"eventstore_current_incoming_grpc_calls{",
		"eventstore_disk_io_bytes{",
		"eventstore_disk_io_operations{",
		"eventstore_elections_count{",
		"eventstore_gc_collection_count{",
		"eventstore_gc_generation_size_bytes{",
		"eventstore_gc_heap_fragmentation{",
		"eventstore_gc_heap_size_bytes{",
		"eventstore_gc_pause_duration_max_seconds{",
		"eventstore_gc_time_in_gc{",
		"eventstore_gc_total_allocated{",
		"eventstore_incoming_grpc_calls{",
		"eventstore_io_bytes{",
		"eventstore_io_events{",
		"eventstore_logical_chunk_read_distribution_bucket{",
		"eventstore_logical_chunk_read_distribution_count{",
		"eventstore_logical_chunk_read_distribution_sum{",
		"eventstore_proc_contention_count{",
		"eventstore_proc_cpu{",
		"eventstore_proc_exception_count{",
		"eventstore_proc_mem_bytes{",
		"eventstore_proc_thread_count{",
		"eventstore_proc_thread_pool_pending_work_item_count{",
		"eventstore_proc_up_time{",
		"eventstore_queue_busy_seconds{",
		"eventstore_queue_queueing_duration_max_seconds{",
		"eventstore_statuses{",
		"eventstore_sys_cpu{",
		"eventstore_sys_disk_bytes{",
		"eventstore_sys_mem_bytes{",
		"eventstore_writer_flush_duration_max_seconds{",
		"eventstore_writer_flush_size_max{",
	];
}
