// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime.Internal;
using Amazon.S3;
using Amazon.S3.Model;
using DotNext.Buffers;
using EventStore.Common.Exceptions;
using FluentStorage;
using FluentStorage.AWS.Blobs;
using Serilog.Events;

namespace EventStore.Core.Services.Archive.Storage.S3;

public class S3BlobStorage : IBlobStorage {
	private readonly S3Options _options;
	private readonly IAwsS3BlobStorage _awsBlobStorage;

	static S3BlobStorage() {
		AWSConfigs.AddTraceListener("Amazon", new AmazonTraceSerilogger(adjustLevel: (level, ex) =>
			ex is AmazonS3Exception { ErrorCode: "NoSuchKey" or "InvalidRange" }
				or HttpErrorResponseException
				? LogEventLevel.Verbose
				: level == LogEventLevel.Error
					? LogEventLevel.Warning
					: level
		));
		AWSConfigs.LoggingConfig.LogTo = LoggingOptions.SystemDiagnostics;
		AWSConfigs.LoggingConfig.LogResponses = ResponseLoggingOption.OnError;
		AWSConfigs.LoggingConfig.LogMetrics = false;
	}

	public S3BlobStorage(S3Options options) {
		_options = options;

		if (string.IsNullOrEmpty(options.Bucket))
			throw new InvalidConfigurationException("Please specify an Archive S3 Bucket");

		if (string.IsNullOrEmpty(options.Region))
			throw new InvalidConfigurationException("Please specify an Archive S3 Region");

		_awsBlobStorage = StorageFactory.Blobs.AwsS3(
			bucketName: options.Bucket,
			region: options.Region) as IAwsS3BlobStorage;
	}

	public async ValueTask<int> ReadAsync(string name, Memory<byte> buffer, long offset, CancellationToken ct) {
		ArgumentOutOfRangeException.ThrowIfNegative(offset);

		var request = new GetObjectRequest {
			BucketName = _options.Bucket,
			Key = name,
			ByteRange = GetRange(offset, buffer.Length),
		};

		try {
			using var response = await _awsBlobStorage.NativeBlobClient.GetObjectAsync(request, ct);
			buffer = buffer.TrimLength(int.CreateSaturating(response.ContentLength));
			await using var responseStream = response.ResponseStream;
			await responseStream.ReadExactlyAsync(buffer, ct);
			return buffer.Length;
		} catch (AmazonS3Exception ex) when (ex.ErrorCode is "NoSuchKey") {
			throw new FileNotFoundException();
		} catch (AmazonS3Exception ex) when (ex.ErrorCode is "InvalidRange") {
			return 0;
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
		return new(Size: response.ContentLength);
	}
}
