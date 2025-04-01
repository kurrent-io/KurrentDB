// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;

namespace EventStore.Security.EncryptionAtRest.Transforms.AesGcm;


public sealed class NonClosingStreamWrapper(Stream innerStream) : Stream {
	public override void Flush() => innerStream.Flush();
	public override int Read(byte[] buffer, int offset, int count) => innerStream.Read(buffer, offset, count);
	public override long Seek(long offset, SeekOrigin origin) => innerStream.Seek(offset, origin);
	public override void SetLength(long value) => innerStream.SetLength(value);
	public override void Write(byte[] buffer, int offset, int count) => innerStream.Write(buffer, offset, count);

	public override bool CanRead => innerStream.CanRead;
	public override bool CanSeek => innerStream.CanSeek;
	public override bool CanWrite => innerStream.CanWrite;
	public override long Length => innerStream.Length;
	public override long Position { get => innerStream.Position; set => innerStream.Position = value; }

	protected override void Dispose(bool disposing) {
		// we don't dispose the inner stream
	}
}
