// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext.Buffers;
using DotNext.Net;
using Google.Protobuf;

namespace KurrentDB.KontrolPlane;

internal static class EndPointExtensions {
	public static ByteString ToByteString(this EndPoint ep) {
		var writer = new BufferWriterSlim<byte>(stackalloc byte[256]);
		try {
			writer.WriteEndPoint(ep);
			return ByteString.CopyFrom(writer.WrittenSpan);
		} finally {
			writer.Dispose();
		}
	}

	public static EndPoint ToEndPoint(this ByteString bytes) {
		var reader = new SequenceReader(bytes.Memory);
		return reader.ReadEndPoint();
	}
}
