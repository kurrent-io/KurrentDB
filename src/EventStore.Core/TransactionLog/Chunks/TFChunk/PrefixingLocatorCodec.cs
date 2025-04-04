// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

// Prefixes remote chunks. Leaves local chunks as-is
public class PrefixingLocatorCodec : ILocatorCodec {
	const string RemotePrefix = "archived-chunk-";

	private readonly int _remotePrefixLength = RemotePrefix.Length;

	public string EncodeLocal(string fileName) => fileName;

	public string EncodeRemote(int chunkNumber) => $"{RemotePrefix}{chunkNumber}";

	public bool Decode(string locator, out int chunkNumber, out string fileName) {
		if (locator.StartsWith(RemotePrefix)) {
			// remote
			chunkNumber = int.Parse(locator[_remotePrefixLength..]);
			fileName = default;
			return true;
		}

		// local
		fileName = locator;
		chunkNumber = default;
		return false;
	}
}
