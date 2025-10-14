// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Services.Archive.Naming;
using KurrentDB.Core.Services.Archive.Storage.Azure;
using KurrentDB.Core.Services.Archive.Storage.Gcp;
using KurrentDB.Core.Services.Archive.Storage.S3;

namespace KurrentDB.Core.Services.Archive.Storage;

public static class ArchiveStorageFactory {
	private const string ArchiveCheckpointFile = "archive.chk";

	public static IArchiveStorage Create(ArchiveOptions options, IArchiveNamingStrategy namingStrategy) {
		IBlobStorage storage = options.StorageType switch {
			StorageType.Unspecified => throw new InvalidOperationException("Please specify an Archive StorageType"),
			StorageType.FileSystemDevelopmentOnly => new FileSystemBlobStorage(options.FileSystem),
			StorageType.S3 => new S3BlobStorage(options.S3),
			StorageType.Azure => new AzureBlobStorage(options.Azure),
			StorageType.GCP => new GcpBlobStorage(options.GCP),
			_ => throw new ArgumentOutOfRangeException(nameof(options.StorageType))
		};

		return new ArchiveStorage(storage, namingStrategy, ArchiveCheckpointFile);
	}
}
