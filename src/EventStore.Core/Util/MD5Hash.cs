// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Buffers;
using DotNext.Collections.Generic;
using EventStore.Common.Utils;
using MD5 = EventStore.Core.Hashing.MD5;

namespace EventStore.Core.Util;

public class MD5Hash {
	public static byte[] GetHashFor(Stream s) {
		//when using this, it will calculate from this point to the END of the stream!
		using (var md5 = MD5.Create())
			return md5.ComputeHash(s);
	}

	public static byte[] GetHashFor(Stream s, int startPosition, long count) {
		Ensure.Nonnegative(count, "count");

		using (var md5 = MD5.Create()) {
			ContinuousHashFor(md5, s, startPosition, count);
			md5.TransformFinalBlock(Empty.ByteArray, 0, 0);
			return md5.Hash;
		}
	}

	public static void ContinuousHashFor(HashAlgorithm md5, Stream s, int startPosition, long count) {
		Ensure.NotNull(md5, "md5");
		Ensure.Nonnegative(count, "count");

		if (s.Position != startPosition)
			s.Position = startPosition;

		var buffer = new byte[4096];
		long toRead = count;
		while (toRead > 0) {
			int read = s.Read(buffer, 0, (int)Math.Min(toRead, buffer.Length));
			if (read == 0)
				break;

			md5.TransformBlock(buffer, 0, read, null, 0);
			toRead -= read;
		}
	}

	public static async ValueTask ContinuousHashFor(IncrementalHash md5, Stream s, int startPosition, long count, CancellationToken token) {
		Ensure.NotNull(md5, "md5");
		Ensure.Nonnegative(count, "count");

		if (s.Position != startPosition)
			s.Position = startPosition;

		var buffer = ArrayPool<byte>.Shared.Rent(4096);
		try {
			for (int bytesRead; count > 0L; count -= bytesRead) {
				bytesRead = await s.ReadAsync(buffer.AsMemory(0, (int)Math.Min(count, buffer.Length)), token);
				if (bytesRead is 0)
					break;

				md5.AppendData(buffer, 0, bytesRead);
			}
		} finally {
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}
}
