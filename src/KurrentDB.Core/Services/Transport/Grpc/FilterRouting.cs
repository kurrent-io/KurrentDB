// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Grpc;

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Services.Transport.Grpc;

/// <summary>
/// Detects when a stream-identifier prefix filter actually targets a secondary index
/// and extracts the index name so the caller can route the request to the index service.
/// </summary>
internal static class FilterRouting {
	/// <summary>
	/// If <paramref name="isStreamIdentifier"/> is true, the regex is empty, and
	/// exactly one prefix matches <see cref="SystemStreams.IsIndexStream"/>,
	/// returns the index name via <paramref name="indexName"/>.
	/// </summary>
	public static bool TryGetIndexName(
		bool isStreamIdentifier,
		string regex,
		IReadOnlyList<string> prefixes,
		[NotNullWhen(true)] out string indexName) {
		indexName = null;

		if (!isStreamIdentifier || !string.IsNullOrEmpty(regex))
			return false;

		if (prefixes is not { Count: > 0 })
			return false;

		var candidate = prefixes[0];
		if (!SystemStreams.IsIndexStream(candidate))
			return false;

		if (prefixes.Count > 1)
			throw RpcExceptions.InvalidArgument(
				"Index reads only work with one index name and cannot be combined with stream prefixes or other indexes");

		indexName = candidate;
		return true;
	}
}
