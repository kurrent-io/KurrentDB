// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext.Buffers;
using DotNext.Net;
using Google.Protobuf;
using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.StateMachine;

internal static class EndPointExtensions {
	public static EndPoint ToEndPoint(this Blob bytes) {
		var reader = new SequenceReader(bytes.Reference.AsMemory());
		return reader.ReadEndPoint();
	}

	public static EndPoint? ToEndPointOrNull(this Blob bytes)
		=> bytes.Reference.Length is 0 ? null : ToEndPoint(bytes);

	public static ByteString ToByteString(this EndPoint ep) {
		var writer = new BufferWriterSlim<byte>(stackalloc byte[256]);
		try {
			writer.WriteEndPoint(ep);
			return ByteString.CopyFrom(writer.WrittenSpan);
		} finally {
			writer.Dispose();
		}
	}
}
