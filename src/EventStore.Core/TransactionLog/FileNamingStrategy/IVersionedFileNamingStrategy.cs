// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog.FileNamingStrategy;

// This abstracts a translation from logical chunk numbers (indexes) and
// chunk versions, to chunk files in storage. Strictly it is more than a naming strategy
// because it also reads the storage to list the chunks.
public interface IVersionedFileNamingStrategy {
	// Pure naming strategy
	string Prefix { get; }
	string GetFilenameFor(int index, int version);
	string CreateTempFilename();
	int GetIndexFor(ReadOnlySpan<char> fileName);
	int GetVersionFor(ReadOnlySpan<char> fileName);

	// Methods that rely on the state of the storage
	string[] GetAllVersionsFor(int index);
	string[] GetAllTempFiles();

	// When we are creating a new version of a chunk at the given index, this determines
	// the correct file name for it based on the existing version(s).
	// defaultVersion is used if there are no existing versions.
	string DetermineNewVersionFilenameForIndex(int index, int defaultVersion);
}
