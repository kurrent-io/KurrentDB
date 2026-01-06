// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Compression;

namespace KurrentDB.Common.Compression;

/// <summary>
/// Workaround for .NET 9+ GZipStream not writing gzip header/footer for empty responses.
/// See: https://github.com/dotnet/runtime/pull/94433
///
/// Valid gzip requires header/footer per RFC 1952, but .NET 9+ now emits 0 bytes.
/// This breaks some gRPC clients expecting complete gzip payloads.
/// </summary>
public class NonEmptyGzipCompressionProvider(CompressionLevel level) : ICompressionProvider {
	public string EncodingName => "gzip";

	public Stream CreateCompressionStream(Stream outputStream, CompressionLevel? compressionLevel) =>
		new CustomGzipStream(outputStream, compressionLevel ?? level);

	public Stream CreateDecompressionStream(Stream compressedStream) =>
		new GZipStream(compressedStream, CompressionMode.Decompress, leaveOpen: true);

	class CustomGzipStream(Stream outputStream, CompressionLevel compressionLevel) : GZipStream(outputStream, compressionLevel, leaveOpen: true) {
		Stream OutputStream { get; } = outputStream ?? throw new ArgumentNullException(nameof(outputStream));

		bool HasContent { get; set; }

		public override void Write(byte[] buffer, int offset, int count) {
			if (count <= 0) return;

			HasContent = true;
			base.Write(buffer, offset, count);
		}

		public override void Write(ReadOnlySpan<byte> buffer) {
			if (buffer.Length <= 0) return;

			HasContent = true;
			base.Write(buffer);
		}

		public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
			if (count <= 0) return Task.CompletedTask;

			HasContent = true;
			return base.WriteAsync(buffer, offset, count, cancellationToken);
		}

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
			if (buffer.Length <= 0) return ValueTask.CompletedTask;

			HasContent = true;
			return base.WriteAsync(buffer, cancellationToken);
		}

		protected override void Dispose(bool disposing) {
			if (disposing && !HasContent)
				WriteEmptyGZipPayload();

			base.Dispose(disposing);
		}

		public override async ValueTask DisposeAsync() {
			if (!HasContent)
				await WriteEmptyGZipPayloadAsync();

			await base.DisposeAsync();
		}

		void WriteEmptyGZipPayload() {
			HasContent = true;

			OutputStream.Write(EmptyGzip.Span);
			OutputStream.Flush();
		}

		async ValueTask WriteEmptyGZipPayloadAsync(CancellationToken cancellationToken = default) {
			HasContent = true;

			await OutputStream.WriteAsync(EmptyGzip, cancellationToken);
			await OutputStream.FlushAsync(cancellationToken);
		}
	}

	// See RFC 1952 (https://datatracker.ietf.org/doc/html/rfc1952)
	static ReadOnlyMemory<byte> EmptyGzip => new byte[] {
		0x1f, 0x8b,             // Magic number
		0x08,                   // Compression method: deflate
		0x00,                   // Flags: none
		0x00, 0x00, 0x00, 0x00, // Modification time: 0 (unknown)
		0x00,                   // Extra flags: none
		0xff,                   // Operating system: unknown (255)
		0x03, 0x00,             // Block header: no compression, 0 bytes
		0x00, 0x00,             // LEN = 0, NLEN = 0 (complement)
		0x00, 0x00, 0x00, 0x00, // CRC-32 for empty data
		0x00, 0x00, 0x00, 0x00  // Size: 0 bytes
	};
}
