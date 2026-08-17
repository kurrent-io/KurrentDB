// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace Kurrent.Kontext.Modules.Entities.Resolution;

/// <summary>
/// The one seam anything other than the projector reaches the entity read model's WRITE surface
/// through: <see cref="KontextEntityProjectorService"/> binds its dedicated connection here for
/// the lifetime of its consumer loop and takes every batch turn through <see cref="RunAsync"/>,
/// so a resolution turn and a batch apply can never overlap.
///
/// A turn, not a lock on the tables: the storage layer offers no row or table locks to build on,
/// and none are needed — the entity tables have exactly ONE writer per node (the projector's
/// connection, which writers never rent, see <see cref="Infrastructure.Data.KontextDataSource"/>),
/// and a DuckDB connection is not thread safe. Mutual exclusion over that ONE connection IS the
/// serialization the storage layer requires, so this gate hands out turns on it instead of
/// pretending to coordinate writers the engine cannot see. The scope is therefore in-process and
/// per-connection, which covers everything that legally writes these tables on this node.
///
/// A turn spans the WHOLE batch — project, apply, checkpoint — not just the writes: the
/// projection's resolution reads run on the same connection (see the
/// <see cref="KontextEntityProjection"/> constructor doc), and a merge landing between those reads
/// and the apply they feed would have the batch decide against a store that no longer exists. A
/// resolution turn therefore waits out at most one batch, the right trade for a reviewer who
/// arrives minutes or days after the doubt was filed.
/// </summary>
public sealed class EntityWriteGate : IDisposable {
	readonly SemaphoreSlim _turn = new(1, 1);

	DuckDBAdvancedConnection? _connection;

	/// <summary>Whether a projector is bound right now — false means the write surface does not exist yet.</summary>
	public bool IsBound => Volatile.Read(ref _connection) is not null;

	/// <summary>
	/// Binds the projector's write connection for the lifetime of the returned scope. One binding
	/// at a time: a second bound connection would be a second writer, which the single-ordered-loop
	/// rule forbids.
	/// </summary>
	public IDisposable Bind(DuckDBAdvancedConnection connection) {
		ArgumentNullException.ThrowIfNull(connection);

		if (Interlocked.CompareExchange(ref _connection, connection, null) is not null)
			throw new InvalidOperationException("The entity write gate is already bound: the entity read model has exactly one writer.");

		return new Binding(this);
	}

	/// <summary>Runs one turn on the bound connection. Turns never overlap, in either direction.</summary>
	public async Task RunAsync(Func<DuckDBAdvancedConnection, CancellationToken, ValueTask> turn, CancellationToken ct = default) {
		await _turn.WaitAsync(ct).ConfigureAwait(false);

		try {
			await turn(Bound(), ct).ConfigureAwait(false);
		} finally {
			_turn.Release();
		}
	}

	/// <inheritdoc cref="RunAsync(Func{DuckDBAdvancedConnection,CancellationToken,ValueTask},CancellationToken)"/>
	public async Task<T> RunAsync<T>(Func<DuckDBAdvancedConnection, CancellationToken, ValueTask<T>> turn, CancellationToken ct = default) {
		await _turn.WaitAsync(ct).ConfigureAwait(false);

		try {
			return await turn(Bound(), ct).ConfigureAwait(false);
		} finally {
			_turn.Release();
		}
	}

	public void Dispose() => _turn.Dispose();

	DuckDBAdvancedConnection Bound() =>
		Volatile.Read(ref _connection)
	 ?? throw new InvalidOperationException(
			"No entity write connection is bound: the entity projector is not running, so the entity read model has no write surface.");

	// Unbinding takes the turn first: an in-flight turn is mid-statement on the connection the
	// projector is about to dispose, and a timeout here would trade a bounded wait for a
	// use-after-dispose. A turn queued behind this one then finds nothing bound and says so.
	sealed class Binding(EntityWriteGate gate) : IDisposable {
		public void Dispose() {
			gate._turn.Wait();

			try {
				Volatile.Write(ref gate._connection, null);
			} finally {
				gate._turn.Release();
			}
		}
	}
}
