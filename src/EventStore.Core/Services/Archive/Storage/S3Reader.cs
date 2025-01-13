// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using DotNext.Buffers;
using EventStore.Common.Exceptions;
using EventStore.Core.Services.Archive.Naming;
using EventStore.Core.Services.Archive.Storage.Exceptions;
using FluentStorage;
using FluentStorage.AWS.Blobs;
using FluentStorage.Blobs;
using Serilog;

namespace EventStore.Core.Services.Archive.Storage;

public class S3Reader : FluentReader, IArchiveStorageReader {
	private readonly S3Options _options;
	private readonly IAwsS3BlobStorage _awsBlobStorage;

	public S3Reader(
		S3Options options,
		IArchiveChunkNameResolver chunkNameResolver,
		string archiveCheckpointFile)
		: base(chunkNameResolver, archiveCheckpointFile) {

		_options = options;

		if (string.IsNullOrEmpty(options.Bucket))
			throw new InvalidConfigurationException("Please specify an Archive S3 Bucket");

		if (string.IsNullOrEmpty(options.Region))
			throw new InvalidConfigurationException("Please specify an Archive S3 Region");

		_awsBlobStorage = StorageFactory.Blobs.AwsS3(
			awsCliProfileName: options.AwsCliProfileName,
			bucketName: options.Bucket,
			region: options.Region) as IAwsS3BlobStorage;
	}

	protected override ILogger Log { get; } = Serilog.Log.ForContext<S3Reader>();

	protected override IBlobStorage BlobStorage => _awsBlobStorage;

	public async ValueTask<int> ReadAsync(int logicalChunkNumber, Memory<byte> buffer, int offset, CancellationToken ct) {
		var chunkFile = await ChunkNameResolver.ResolveFileName(logicalChunkNumber, ct);
		var request = new GetObjectRequest {
			BucketName = _options.Bucket,
			Key = chunkFile,
			ByteRange = GetRange(offset, buffer.Length),
		};

		try {
			var client = _awsBlobStorage.NativeBlobClient;
			using var response = await client.GetObjectAsync(request, ct);
			var length = int.CreateSaturating(response.ContentLength);
			await using var responseStream = response.ResponseStream;
			await responseStream.ReadExactlyAsync(buffer.TrimLength(length), ct);
			return length;
		} catch (AmazonS3Exception ex) {
			if (ex.ErrorCode == "NoSuchKey")
				throw new ChunkDeletedException();
			throw;
		}
	}

	// ByteRange is inclusive of both start and end
	private static ByteRange GetRange(long offset, int length) => new(
		start: offset,
		end: offset + length - 1L);
}
