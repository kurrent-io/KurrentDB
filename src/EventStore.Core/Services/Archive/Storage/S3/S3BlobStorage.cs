// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using DotNext.Buffers;
using EventStore.Common.Exceptions;
using FluentStorage;
using FluentStorage.AWS.Blobs;
using Serilog;

namespace EventStore.Core.Services.Archive.Storage.S3;

public class S3BlobStorage : IBlobStorage {
	protected static readonly ILogger Log = Serilog.Log.ForContext<S3BlobStorage>();

	private readonly S3Options _options;
	private readonly IAwsS3BlobStorage _awsBlobStorage;

	public S3BlobStorage(S3Options options) {
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

	//qqqq there are two approaches,
	// option 1: version could specify they version that we want to read, and it forms part of the request
	//          and we retain old versions for say a day
	//
	// option 2: the version can be the expected version and we throw if the actual version is different
	//
	// option 3: we could return the version that was actually read and allow the caller to tell
	// 
	//
	// somewhere somehow we need to instantiate a new TFChunk pointing to the new version
	//qq what happens if the offset is too big (could happen if the blob has been replaced with a smaller one)

	public async ValueTask<(int, string)> ReadAsync(string name, Memory<byte> buffer, long offset, CancellationToken ct) {
		var request = new GetObjectRequest {
			BucketName = _options.Bucket,
			Key = name,
			ByteRange = GetRange(offset, buffer.Length),
		};

		try {
			using var response = await _awsBlobStorage.NativeBlobClient.GetObjectAsync(request, ct);
			var length = int.CreateSaturating(response.ContentLength);
			await using var responseStream = response.ResponseStream;

			//response.VersionId;
			//response.ETag;
			//response.ChecksumSHA1;
			//response.DeleteMarker;
			//response.Metadata;

			await responseStream.ReadExactlyAsync(buffer.TrimLength(length), ct);
			return (length, response.ETag); //qqq dunno if the ETag is what we want
		} catch (AmazonS3Exception ex) when (ex.ErrorCode is "NoSuchKey") {
			throw new FileNotFoundException();
		}
	}

	public ValueTask StoreAsync(Stream readableStream, string name, CancellationToken ct)
		=> new(_awsBlobStorage.WriteAsync(name, readableStream, append: false, ct));

	// ByteRange is inclusive of both start and end
	private static ByteRange GetRange(long offset, int length) => new(
		start: offset,
		end: offset + length - 1L);

	public async ValueTask<BlobMetadata> GetMetadataAsync(string name, CancellationToken token) {
		var response = await _awsBlobStorage.NativeBlobClient.GetObjectMetadataAsync(
			_awsBlobStorage.BucketName, name, token);
		return new(
			Size: response.ContentLength,
			ETag: response.ETag); //qqq check this is the etag of the blob
	}
}
