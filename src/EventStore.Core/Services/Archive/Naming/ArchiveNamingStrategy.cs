// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using EventStore.Core.TransactionLog.FileNamingStrategy;

namespace EventStore.Core.Services.Archive.Naming;

public class ArchiveNamingStrategy : IArchiveNamingStrategy {
	private readonly IVersionedFileNamingStrategy _namingStrategy;

	public ArchiveNamingStrategy(IVersionedFileNamingStrategy namingStrategy) {
		_namingStrategy = namingStrategy;
	}

	public string Prefix => _namingStrategy.Prefix;

	public string GetBlobNameFor(int logicalChunkNumber) {
		// naming chunks remotely in a way that is compatible with locally allows us
		// to easily download remote chunks and use them locally.
		var filePath = _namingStrategy.GetFilenameFor(logicalChunkNumber, version: 1);
		return Path.GetFileName(filePath);
	}
}
