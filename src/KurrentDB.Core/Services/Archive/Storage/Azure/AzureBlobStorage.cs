// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DotNext.Buffers;
using Serilog;

namespace KurrentDB.Core.Services.Archive.Storage.Azure;

public class AzureBlobStorage : IBlobStorage {
	private static readonly ILogger Logger = Log.ForContext<AzureBlobStorage>();

	public AzureBlobStorage(AzureOptions options) => NativeClient = options.CreateClient();

	public BlobContainerClient NativeClient { get; }

	public async ValueTask<int> ReadAsync(string name, Memory<byte> buffer, long offset, CancellationToken token) {
		ArgumentOutOfRangeException.ThrowIfNegative(offset);

		// The check is required, otherwise, HttpRange throws ArgumentOutOfRangeException
		if (buffer.IsEmpty)
			return 0;

		var blobClient = NativeClient.GetBlobClient(name);
		var blobStream = default(Stream);
		try {
			var response = await blobClient.DownloadStreamingAsync(new BlobDownloadOptions {
				Range = new(offset, buffer.Length)
			}, token);

			buffer = buffer.TrimLength(int.CreateSaturating(response.Value.Details.ContentLength));
			blobStream = response.Value.Content;
			await blobStream.ReadExactlyAsync(buffer, token);
			return buffer.Length;
		} catch (RequestFailedException ex) when (ex.Status is (int)HttpStatusCode.NotFound) {
			throw new FileNotFoundException();
		} catch (RequestFailedException ex) when (ex.Status is (int)HttpStatusCode.RequestedRangeNotSatisfiable) {
			return 0;
		} catch (RequestFailedException ex) {
			Logger.Error(ex, "Failed to read object '{name}' at offset: {offset}, length: {length}", name, offset, buffer.Length);
			throw;
		} finally {
			if (blobStream is not null)
				await blobStream.DisposeAsync();
		}
	}

	public async ValueTask<BlobMetadata> GetMetadataAsync(string name, CancellationToken token) {
		var blobClient = NativeClient.GetBlobClient(name);
		BlobProperties metadata;
		try {
			metadata = await blobClient.GetPropertiesAsync(cancellationToken: token);
		} catch (RequestFailedException ex) {
			Logger.Error(ex, "Failed to fetch metadata for object '{name}'", name);
			throw;
		}

		return new(metadata.ContentLength);
	}

	public async ValueTask StoreAsync(Stream readableStream, string name, CancellationToken ct) {
		var blobClient = NativeClient.GetBlobClient(name);
		try {
			await blobClient.UploadAsync(readableStream, ct);
		} catch (RequestFailedException ex) {
			Logger.Error(ex, "Failed to store object '{name}'", name);
			throw;
		}
	}
}
