// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Collections.Concurrent;
using Google.Protobuf;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Tcp;

public static class ProtobufExtensions {
	private static readonly ConcurrentStack<MemoryStream> _streams;

	static ProtobufExtensions() {
		_streams = new ConcurrentStack<MemoryStream>();
		for (var i = 0; i < 300; i++) {
			_streams.Push(new MemoryStream(2048));
		}
	}

	private static readonly ILogger Log =
		Serilog.Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "ProtobufExtensions");

	static MemoryStream AcquireStream() {
		for (var i = 0; i < 1000; i++) {
			MemoryStream ret;
			if (_streams.TryPop(out ret)) {
				ret.SetLength(0);
				return ret;
			}

			if ((i + 1) % 5 == 0)
				Thread.Sleep(1); //need to do better than this
		}

		throw new UnableToAcquireStreamException();
	}

	static void ReleaseStream(MemoryStream stream) {
		_streams.Push(stream);
	}

	public static T Deserialize<T>(this byte[] data) where T: IMessage<T>, new() {
		return Deserialize<T>(new ArraySegment<byte>(data));
	}

	public static T Deserialize<T>(this ArraySegment<byte> data) where T: IMessage<T>, new() {
		try {
			using (var memory = new MemoryStream(data.Array, data.Offset, data.Count)
			) //uses original buffer as memory
			{
				var res = new T();
				res.MergeFrom(memory);
				return res;
			}
		} catch (Exception e) {
			Log.Information(e, "Deserialization to {type} failed", typeof(T).FullName);
			return default(T);
		}
	}

	public static ArraySegment<byte> Serialize<T>(this T protoContract) where T: IMessage<T> {
		MemoryStream stream = null;
		try {
			stream = AcquireStream();
			protoContract.WriteTo(stream);
			var res = new ArraySegment<byte>(stream.ToArray(), 0, (int)stream.Length);
			return res;
		} finally {
			if (stream != null) {
				ReleaseStream(stream);
			}
		}
	}

	public static byte[] SerializeToArray<T>(this T protoContract) where T: IMessage<T>{
		MemoryStream stream = null;
		try {
			stream = AcquireStream();
			protoContract.WriteTo(stream);
			return stream.ToArray();
		} finally {
			if (stream != null) {
				ReleaseStream(stream);
			}
		}
	}
}
