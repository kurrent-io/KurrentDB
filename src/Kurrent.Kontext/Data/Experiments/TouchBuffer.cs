// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.VectorData;

namespace Kurrent.Kontext.Data;

/// <summary>
/// Write-behind buffer for reconsolidation touches (see <see cref="TouchBufferOptions"/> for the
/// semantics and trade-offs). Touches coalesce per memory id — last touch wins — and flush as one
/// batch upsert when the pending set reaches <see cref="TouchBufferOptions.BatchSize"/>, when the
/// oldest pending touch has waited <see cref="TouchBufferOptions.BatchWait"/>, or on dispose.
/// </summary>
sealed class TouchBuffer(VectorStoreCollection<string, MemoryRecord> collection, TouchBufferOptions options) : IDisposable, IAsyncDisposable {
    readonly Lock                             _lock    = new();
    readonly Dictionary<string, MemoryRecord> _pending = [];

    Timer? _timer;
    Task   _flushes = Task.CompletedTask;
    bool   _timerArmed;
    bool   _disposed;

    public void Add(IReadOnlyList<MemoryRecord> records, DateTimeOffset touchedAt) {
        List<MemoryRecord>? full = null;

        lock (_lock) {
            // Late touches during shutdown are dropped — losing a recency refresh is the accepted cost.
            if (_disposed)
                return;

            foreach (var record in records) {
                record.LastAccessedAt     = touchedAt;
                _pending[record.MemoryId] = record;
            }

            if (_pending.Count >= options.BatchSize) {
                full = TakePendingLocked();
            } else if (!_timerArmed) {
                // The linger clock starts with the first touch of an empty buffer and is not extended
                // by later arrivals, so a steady trickle still flushes every BatchWait.
                _timer ??= new Timer(
                    static state => ((TouchBuffer)state!).OnBatchWaitElapsed(), this, Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);

                _timer.Change(options.BatchWait, Timeout.InfiniteTimeSpan);
                _timerArmed = true;
            }
        }

        if (full is not null)
            Flush(full);
    }

    void OnBatchWaitElapsed() {
        List<MemoryRecord>? pending;

        lock (_lock) {
            if (_disposed)
                return;

            pending = _pending.Count > 0 ? TakePendingLocked() : null;
        }

        if (pending is not null)
            Flush(pending);
    }

    // Must be called under _lock.
    List<MemoryRecord> TakePendingLocked() {
        var taken = _pending.Values.ToList();
        _pending.Clear();
        _timerArmed = false;
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return taken;
    }

    // Tracked so DisposeAsync can await every in-flight flush, making shutdown deterministic.
    void Flush(List<MemoryRecord> records) {
        var flush = FlushAsync(records);

        lock (_lock)
            _flushes = Task.WhenAll(_flushes, flush);
    }

    async Task FlushAsync(List<MemoryRecord> records) {
        try {
            // Not a request token: the flush outlives the request that buffered the touch.
            await collection.UpsertAsync(records, CancellationToken.None).ConfigureAwait(false);
        } catch {
            // Dropping the batch IS the recovery: touches are loss-tolerant by contract, and the
            // requests that buffered them have long since returned. Wire this to a logger once the
            // service grows one.
        }
    }

    public async ValueTask DisposeAsync() {
        List<MemoryRecord> remaining;
        Task               flushes;

        lock (_lock) {
            if (_disposed)
                return;

            _disposed = true;
            remaining = _pending.Values.ToList();
            _pending.Clear();
            flushes = _flushes;
        }

        // Awaiting the timer's disposal guarantees no callback is still running before the final flush.
        if (_timer is not null)
            await _timer.DisposeAsync().ConfigureAwait(false);

        await flushes.ConfigureAwait(false);

        if (remaining.Count > 0)
            await FlushAsync(remaining).ConfigureAwait(false);
    }

    public void Dispose() {
        // Best-effort sync path for containers that dispose synchronously: the remaining touches are
        // fired off without being awaited — acceptable for a loss-tolerant clock refresh, and it
        // avoids sync-over-async blocking. Prefer DisposeAsync.
        List<MemoryRecord>? remaining;

        lock (_lock) {
            if (_disposed)
                return;

            _disposed = true;
            remaining = _pending.Count > 0 ? _pending.Values.ToList() : null;
            _pending.Clear();
        }

        _timer?.Dispose();

        if (remaining is not null)
            _ = FlushAsync(remaining);
    }
}