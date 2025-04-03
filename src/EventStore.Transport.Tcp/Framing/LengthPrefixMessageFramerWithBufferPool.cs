// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.BufferManagement;
using EventStore.Common.Utils;
using ILogger = Serilog.ILogger;

namespace EventStore.Transport.Tcp.Framing;

public class LengthPrefixMessageFramerWithBufferPool {
	private static readonly ILogger Log = Serilog.Log.ForContext<LengthPrefixMessageFramerWithBufferPool>();

	private const int PrefixLength = sizeof(int);

	private readonly int _maxPackageSize;
	private readonly BufferManager _bufferManager;
	private BufferPool _messageBuffer;
	private Action<BufferPool> _receivedHandler;

	private int _headerBytes;
	private int _packageLength;

	/// <summary>
	/// Initializes a new instance of the <see cref="LengthPrefixMessageFramerWithBufferPool"/> class.
	/// </summary>
	public LengthPrefixMessageFramerWithBufferPool(BufferManager bufferManager,
		int maxPackageSize = 16 * 1024 * 1024) {
		Ensure.NotNull(bufferManager, "bufferManager");
		Ensure.Positive(maxPackageSize, "maxPackageSize");
		_bufferManager = bufferManager;
		_maxPackageSize = maxPackageSize;
	}

	public void Reset() {
		_messageBuffer = null;
		_headerBytes = 0;
		_packageLength = 0;
	}

	public void UnFrameData(IEnumerable<ArraySegment<byte>> data) {
		if (data == null)
			throw new ArgumentNullException("data");

		foreach (ArraySegment<byte> buffer in data) {
			Parse(buffer);
		}
	}

	public void UnFrameData(ArraySegment<byte> data) {
		Parse(data);
	}

	/// <summary>
	/// Parses a stream chunking based on length-prefixed framing. Calls are re-entrant and hold state internally.
	/// </summary>
	/// <param name="bytes">A byte array of data to append</param>
	private void Parse(ArraySegment<byte> bytes) {
		byte[] data = bytes.Array;
		for (int i = bytes.Offset; i < bytes.Offset + bytes.Count;) {
			if (_headerBytes < PrefixLength) {
				_packageLength |= (data[i] << (_headerBytes * 8)); // little-endian order
				++_headerBytes;
				i += 1;
				if (_headerBytes == PrefixLength) {
					if (_packageLength <= 0 || _packageLength > _maxPackageSize) {
						Log.Error("FRAMING ERROR! Data:\n {data}", Common.Utils.Helper.FormatBinaryDump(bytes));
						throw new PackageFramingException(string.Format(
							"Package size is out of bounds: {0} (max: {1}).",
							_packageLength, _maxPackageSize));
					}

					_messageBuffer = new BufferPool(_bufferManager);
				}
			} else {
				int copyCnt = Math.Min(bytes.Count + bytes.Offset - i, _packageLength - _messageBuffer.Length);
				_messageBuffer.Append(bytes.Array, i, copyCnt);
				i += copyCnt;

				if (_messageBuffer.Length == _packageLength) {
					if (_receivedHandler != null)
						_receivedHandler(_messageBuffer);
					_messageBuffer = null;
					_headerBytes = 0;
					_packageLength = 0;
				}
			}
		}
	}

	public IEnumerable<ArraySegment<byte>> FrameData(ArraySegment<byte> data) {
		var length = data.Count;

		yield return new ArraySegment<byte>(
			new[] {(byte)length, (byte)(length >> 8), (byte)(length >> 16), (byte)(length >> 24)});
		yield return data;
	}

	public void RegisterMessageArrivedCallback(Action<BufferPool> handler) {
		if (handler == null)
			throw new ArgumentNullException("handler");

		_receivedHandler = handler;
	}
}
