// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow;
using DotNext.Buffers;

namespace KurrentDB.SecondaryIndexing.FlightSql;

/// <summary>
/// Represents a cached prepared statement.
/// </summary>
internal sealed class PreparedStatement(in MemoryOwner<byte> preparedQuery, Schema datasetSchema) : IDisposable {
	private static readonly object Sentinel = new();

	private MemoryOwner<byte> _preparedQuery = preparedQuery;
	private volatile object? _parameters = Sentinel;

	// Due to potential concurrency caused by FlightSQL client, this object can be accessed in multiple
	// threads. Once the caller obtained the statement, it needs to increment its counter and then decrement
	// when the statement is no longer needed.
	private volatile int _referenceCounter = 1;

	/// <summary>
	/// Gets the schema of the dataset to be returned by the prepared statement.
	/// </summary>
	public Schema DatasetSchema => datasetSchema;

	/// <summary>
	/// Tries to increment the internal counter.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the counter is incremented successfully and the statement is alive;
	/// <see langword="false"/> if the statement is no longer available.
	/// </returns>
	public bool TryIncrementRef() {
		for (int current = _referenceCounter, tmp;; current = tmp) {
			if (current is 0)
				break;

			tmp = Interlocked.CompareExchange(ref _referenceCounter, current + 1, current);
			if (tmp == current)
				return true;
		}

		return false;
	}

	public void DecrementRef() {
		for (int current = _referenceCounter, tmp;; current = tmp) {
			if (current is 0)
				break;

			tmp = Interlocked.CompareExchange(ref _referenceCounter, current - 1, current);
			if (tmp != current)
				continue;

			if (current is 1)
				Dispose();

			break;
		}
	}

	/// <summary>
	/// Gets the prepared query.
	/// </summary>
	public ReadOnlyMemory<byte> Query => _preparedQuery.Memory;

	/// <summary>
	/// Exclusively captures the batch representing arguments for the statement binding.
	/// </summary>
	/// <returns>The batch that represents arguments for the statement binding; or <see langword="null"/> if no arguments present.</returns>
	public RecordBatch? TryUnbind()
		=> Interlocked.Exchange(ref _parameters, Sentinel) as RecordBatch;

	public bool TryBind(RecordBatch batch, out RecordBatch? oldBatch) {
		oldBatch = null;
		for (object? current = _parameters, tmp;; current = tmp) {
			if (current is null)
				break;

			tmp = Interlocked.CompareExchange(ref _parameters, batch, current);
			if (!ReferenceEquals(tmp, current))
				continue;

			oldBatch = tmp as RecordBatch;
			return true;
		}

		return false;
	}

	private void Dispose(bool disposing) {
		if (disposing) {
			(Interlocked.Exchange(ref _parameters, null) as RecordBatch)?.Dispose();
		}

		_preparedQuery.Dispose();
	}

	public void Dispose() {
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	~PreparedStatement() => Dispose(false);
}
