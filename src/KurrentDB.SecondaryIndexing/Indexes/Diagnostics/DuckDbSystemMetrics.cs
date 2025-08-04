// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using Kurrent.Quack.Diagnostics;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Diagnostics;

public class DuckDbSystemMetrics {
	private readonly Meter _meter;
	private readonly string _meterPrefix;
	private readonly DuckDbDataSource _db;
	private readonly string _dbFile;

	public DuckDbSystemMetrics(Meter meter, string meterPrefix, DuckDbDataSource db, string dbFile) {
		_meter = meter;
		_meterPrefix = meterPrefix;
		_db = db;
		_dbFile = dbFile;

		CreateMetrics();
	}

	private void CreateMetrics() {
		CreateMemoryMetric();
		CreateDiskMetric();
	}

	private void CreateMemoryMetric() {
		_meter.CreateObservableGauge(
			$"{_meterPrefix}.memory",
			ObserveMemoryUsage,
			"bytes",
			"DuckDB memory usage"
		);
	}

	private void CreateDiskMetric() {
		_meter.CreateObservableGauge(
			$"{_meterPrefix}.disk",
			ObserveDiskUsage,
			"bytes",
			"DuckDB disk space usage"
		);
	}

	private IEnumerable<Measurement<long>> ObserveMemoryUsage() {
		var totalMem = 0L;

		using (_db.Pool.Rent(out var connection)) {
			var memoryInfo = connection.GetMemoryInfo();

			yield return GetMeasurement(nameof(memoryInfo.Allocator), memoryInfo.Allocator);
			yield return GetMeasurement(nameof(memoryInfo.ArtIndex), memoryInfo.ArtIndex);
			yield return GetMeasurement(nameof(memoryInfo.BaseTable), memoryInfo.BaseTable);
			yield return GetMeasurement(nameof(memoryInfo.ColumnData), memoryInfo.ColumnData);
			yield return GetMeasurement(nameof(memoryInfo.CsvReader), memoryInfo.CsvReader);
			yield return GetMeasurement(nameof(memoryInfo.Extension), memoryInfo.Extension);
			yield return GetMeasurement(nameof(memoryInfo.HashTable), memoryInfo.HashTable);
			yield return GetMeasurement(nameof(memoryInfo.InMemoryTable), memoryInfo.InMemoryTable);
			yield return GetMeasurement(nameof(memoryInfo.Metadata), memoryInfo.Metadata);
			yield return GetMeasurement(nameof(memoryInfo.OrderByQueries), memoryInfo.OrderByQueries);
			yield return GetMeasurement(nameof(memoryInfo.OverflowStrings), memoryInfo.OverflowStrings);
			yield return GetMeasurement(nameof(memoryInfo.ParquetReader), memoryInfo.ParquetReader);
		}

		yield return new Measurement<long>(totalMem);

		yield break;

		Measurement<long> GetMeasurement(string key, long value) {
			totalMem += value;
			return new Measurement<long>(value, new KeyValuePair<string, object?>("component", key));
		}
	}

	private IEnumerable<Measurement<long>> ObserveDiskUsage() {
		var dbFileSize = new FileInfo(_dbFile).Length;
		yield return new Measurement<long>(dbFileSize, new KeyValuePair<string, object?>("type", "database"));

		using (_db.Pool.Rent(out var connection)) {
			var tmpFileSize = connection.GetTemporaryFileSize();
			yield return new Measurement<long>(tmpFileSize, new KeyValuePair<string, object?>("type", "temporary-files"));
		}
	}
}
