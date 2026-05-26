// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;

namespace KurrentDB.Kontext.Indexing;

/// <summary>
/// A durable index fed by the indexing pipeline, checkpointed by durability watermark.
/// </summary>
public interface IIndexStore {
	/// <summary>
	/// Persists what the store decides is worth persisting and returns its durability watermark:
	/// every add at or before the returned position is on disk; anything after it must be replayed
	/// after a crash. <paramref name="current"/> is the pipeline's position and is returned as-is
	/// when the store has nothing pending. With <paramref name="force"/>, all pending data is
	/// persisted first, so the result is always <paramref name="current"/>.
	/// </summary>
	TFPos Flush(TFPos current, bool force);
}
