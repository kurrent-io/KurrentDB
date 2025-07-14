// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace EventStore.Plugins.Transforms;

public class ChunkDataWriteStream(Stream chunkFileStream, IncrementalHash checksumAlgorithm) : ChunkDataStream(chunkFileStream) {
	private long? _positionToHash;

	public IncrementalHash ChecksumAlgorithm => checksumAlgorithm;

	public sealed override bool CanRead => false;
	public sealed override bool CanSeek => false;
	public sealed override bool CanWrite => true;

	public sealed override int Read(Span<byte> buffer) => throw new NotSupportedException();

	public override void Write(ReadOnlySpan<byte> buffer) {
		if (_positionToHash is { } count) {
			ReadAndChecksum(count);

			Debug.Assert(ChunkFileStream.Position == count);
			_positionToHash = null;
		}

		ChunkFileStream.Write(buffer);
		checksumAlgorithm.AppendData(buffer);
	}

	public override void Flush() => ChunkFileStream.Flush();

	public sealed override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException();

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
		=> _positionToHash is { } count ? WriteAndChecksumAsync(count, buffer, token) : WriteWithoutChecksumAsync(buffer, token);

	[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
	private async ValueTask WriteWithoutChecksumAsync(ReadOnlyMemory<byte> buffer, CancellationToken token) {
		await ChunkFileStream.WriteAsync(buffer, token);
		checksumAlgorithm.AppendData(buffer.Span);
	}

	private async ValueTask WriteAndChecksumAsync(long count, ReadOnlyMemory<byte> buffer, CancellationToken token) {
		await ReadAndChecksumAsync(count, token);

		Debug.Assert(ChunkFileStream.Position == count);
		await ChunkFileStream.WriteAsync(buffer, token);
		checksumAlgorithm.AppendData(buffer.Span);
		_positionToHash = null;
	}

	public override Task FlushAsync(CancellationToken ct) => ChunkFileStream.FlushAsync(ct);
	public override void SetLength(long value) => ChunkFileStream.SetLength(value);
	public override long Length => ChunkFileStream.Length;
	public override long Position {
		get => _positionToHash ?? ChunkFileStream.Position;
		set {
			if (ChunkFileStream.Position is not 0L)
				throw new InvalidOperationException("Writer's position can only be moved from 0 to a higher value.");

			if (value is not 0L)
				_positionToHash = value;
		}
	}

	private async ValueTask ReadAndChecksumAsync(long count, CancellationToken token) {
		var buffer = ArrayPool<byte>.Shared.Rent(4096);

		try {
			for (int bytesRead; count > 0L; count -= bytesRead) {
				bytesRead = await ChunkFileStream.ReadAsync(buffer.AsMemory(0, (int)long.Min(count, buffer.Length)),
					token);
				if (bytesRead is 0)
					break;

				checksumAlgorithm.AppendData(new ReadOnlySpan<byte>(buffer, 0, bytesRead));
			}
		} finally {
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private void ReadAndChecksum(long count) {
		Span<byte> buffer = stackalloc byte[1024];

		for (int bytesRead; count > 0L; count -= bytesRead) {
			bytesRead = ChunkFileStream.Read(buffer.Slice(0, (int)long.Min(count, buffer.Length)));
			if (bytesRead is 0)
				break;

			checksumAlgorithm.AppendData(buffer.Slice(0, bytesRead));
		}
	}
}
