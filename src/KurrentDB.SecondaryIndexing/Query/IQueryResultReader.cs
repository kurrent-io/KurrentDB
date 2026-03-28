// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Query;

public interface IQueryResultReader {
	/// <summary>
	/// Advances to the next chunk.
	/// </summary>
	/// <returns><see langword="true"/> if the chunk is available for consumption; <see langword="false"/> if no more chunks available.</returns>
	bool TryRead();

	/// <summary>
	/// Gets the current chunk.
	/// </summary>
	/// <remarks>
	/// The chunk becomes invalid after <see cref="TryRead"/> call.
	/// </remarks>
	ref readonly DataChunk Chunk { get; }
}
