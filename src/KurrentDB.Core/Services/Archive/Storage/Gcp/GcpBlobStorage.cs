// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Buffers;
using DotNext.IO;
using Google;
using Google.Cloud.Storage.V1;
using KurrentDB.Common.Exceptions;
using Serilog;

namespace KurrentDB.Core.Services.Archive.Storage.Gcp;

public class GcpBlobStorage : IBlobStorage {
	private readonly GcpOptions _options;
	private readonly StorageClient _storageClient;

	private static readonly ILogger Logger = Log.ForContext<GcpBlobStorage>();

	public GcpBlobStorage(GcpOptions options) {
		_options = options;

		if (string.IsNullOrEmpty(options.Bucket))
			throw new InvalidConfigurationException("Please specify an Archive GCP Bucket");

		_storageClient = StorageClient.Create();
	}

	public async ValueTask<int> ReadAsync(string name, Memory<byte> buffer, long offset, CancellationToken ct) {
		ArgumentOutOfRangeException.ThrowIfNegative(offset);

		var writer = new MemoryWriter(buffer);
		var destination = StreamSource.AsSynchronousStream(writer);
		try {
			var obj = await _storageClient.DownloadObjectAsync(
				bucket: _options.Bucket,
				objectName: name,
				destination: destination,
				options: new DownloadObjectOptions {
					Range = GetRange(offset, buffer.Length)
				}, cancellationToken: ct);

			return writer.BytesWritten;
		} catch (GoogleApiException ex) when (
			ex.HttpStatusCode is HttpStatusCode.NotFound &&
			ex.Error.ErrorResponseContent.StartsWith("No such object:")) {
			throw new FileNotFoundException();
		} catch (GoogleApiException ex) when (ex.HttpStatusCode is HttpStatusCode.RequestedRangeNotSatisfiable) {
			return 0;
		} catch (GoogleApiException ex) {
			Logger.Error(ex, "Failed to read object '{name}' at offset: {offset}, length: {length}", name, offset, buffer.Length);
			throw;
		} finally {
			await destination.DisposeAsync();
		}
	}

	public async ValueTask StoreAsync(Stream readableStream, string name, CancellationToken ct) {
		try {
			await _storageClient.UploadObjectAsync(_options.Bucket, name, string.Empty, readableStream, cancellationToken: ct);
		} catch (GoogleApiException ex) {
			Logger.Error(ex, "Failed to store object '{name}'", name);
			throw;
		}
	}

	public async ValueTask<BlobMetadata> GetMetadataAsync(string name, CancellationToken token) {
		try {
			var obj = await _storageClient.GetObjectAsync(_options.Bucket, name, cancellationToken: token);
			return new BlobMetadata(Size: long.CreateSaturating(obj.Size!.Value));
		} catch (GoogleApiException ex) {
			Logger.Error(ex, "Failed to fetch metadata for object '{name}'", name);
			throw;
		}
	}

	private static RangeHeaderValue GetRange(long offset, int length) => new(
		from: offset,
		to: offset + length - 1L);

	private sealed class MemoryWriter(Memory<byte> output) : IReadOnlySpanConsumer<byte>, IFlushable {
		private int _bytesWritten;

		public int BytesWritten => _bytesWritten;

		void IReadOnlySpanConsumer<byte>.Invoke(ReadOnlySpan<byte> input) {
			input.CopyTo(output.Span.Slice(_bytesWritten));
			_bytesWritten += input.Length;
		}

		void IFlushable.Flush() {
			// nothing to do
		}

		Task IFlushable.FlushAsync(CancellationToken token) => Task.CompletedTask;
	}
}
