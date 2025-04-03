// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using RuntimeInformation = System.Runtime.RuntimeInformation;

namespace EventStore.Core.TransactionLog.Unbuffered;

//NOTE THIS DOES NOT SUPPORT ALL STREAM OPERATIONS AS YOU MIGHT EXPECT IT SUPPORTS WHAT WE USE!
public unsafe class UnbufferedFileStream : Stream {
	private static readonly INativeFile NativeFile;

	private byte* _writeBuffer;
	private byte* _readBuffer;
	private readonly int _writeBufferSize;
	private readonly int _readBufferSize;
	private readonly IntPtr _writeBufferOriginal;
	private readonly IntPtr _readBufferOriginal;
	private readonly uint _blockSize;
	private long _bufferedCount;
	private bool _aligned;
	private long _lastPosition;
	private bool _needsFlush;
	private SafeFileHandle _handle;
	private long _readLocation = -1;
	private bool _needsRead;

	static UnbufferedFileStream() => 
            NativeFile = RuntimeInformation.IsWindows ? new NativeFileWindows() : new NativeFileUnix();

        private UnbufferedFileStream(SafeFileHandle handle, uint blockSize, int internalWriteBufferSize,
		int internalReadBufferSize) {
		_handle = handle;
		_readBufferSize = internalReadBufferSize;
		_writeBufferSize = internalWriteBufferSize;
		_writeBufferOriginal = Marshal.AllocHGlobal((int)(internalWriteBufferSize + blockSize));
		_readBufferOriginal = Marshal.AllocHGlobal((int)(internalReadBufferSize + blockSize));
		_readBuffer = Align(_readBufferOriginal, blockSize);
		_writeBuffer = Align(_writeBufferOriginal, blockSize);
		_blockSize = blockSize;
	}

	private byte* Align(IntPtr buf, uint alignTo) {
		//This makes an aligned buffer linux needs this.
		//The buffer must originally be at least one alignment bigger!
		var diff = alignTo - (buf.ToInt64() % alignTo);
		var aligned = (IntPtr)(buf.ToInt64() + diff);
		return (byte*)aligned;
	}

	public static UnbufferedFileStream Create(string path,
		FileMode mode,
		FileAccess acc,
		FileShare share,
		int internalWriteBufferSize,
		int internalReadBufferSize,
		bool writeThrough,
		uint minBlockSize) {
		var blockSize = NativeFile.GetDriveSectorSize(path);
		blockSize = blockSize > minBlockSize ? blockSize : minBlockSize;
		if (internalWriteBufferSize % blockSize != 0)
			throw new Exception("write buffer size must be aligned to block size of " + blockSize + " bytes");
		if (internalReadBufferSize % blockSize != 0)
			throw new Exception("read buffer size must be aligned to block size of " + blockSize + " bytes");

		var handle = NativeFile.CreateUnbufferedRW(path, acc, share, mode, writeThrough);
		return new UnbufferedFileStream(handle, blockSize, internalWriteBufferSize, internalReadBufferSize);
	}

	public override void Flush() {
		CheckDisposed();
		if (!_needsFlush) return;
		var alignedbuffer = (int)GetLowestAlignment(_bufferedCount);
		var positionAligned = GetLowestAlignment(_lastPosition);
		if (!_aligned) {
			SeekInternal(positionAligned, SeekOrigin.Begin);
		}

		if (_bufferedCount == alignedbuffer) {
			InternalWrite(_writeBuffer, (uint)_bufferedCount);
			_lastPosition = positionAligned + _bufferedCount;
			_bufferedCount = 0;
			_aligned = true;
		} else {
			var left = _bufferedCount - alignedbuffer;
			InternalWrite(_writeBuffer, (uint)(alignedbuffer + _blockSize));
			_lastPosition = positionAligned + alignedbuffer;
			SetBuffer(alignedbuffer, left);
			_bufferedCount = left;
			_aligned = false;
		}

		_needsFlush = false;
	}

	private static void MemCopy(byte[] src, long srcOffset, byte* dest, long destOffset, long count) {
		fixed (byte* p = src) {
			MemCopy(p, srcOffset, dest, destOffset, count);
		}
	}

	private static void MemCopy(byte* src, long srcOffset, byte[] dest, long destOffset, long count) {
		fixed (byte* p = dest) {
			MemCopy(src, srcOffset, p, destOffset, count);
		}
	}

	private static void MemCopy(byte* src, long srcOffset, byte* dest, long destOffset, long count) {
		byte* psrc = src + srcOffset;
		byte* pdest = dest + destOffset;

		for (var i = 0; i < count; i++) {
			*pdest = *psrc;
			pdest++;
			psrc++;
		}
	}

	private void SeekInternal(long positionAligned, SeekOrigin origin) {
		NativeFile.Seek(_handle, positionAligned, origin);
	}

	private void InternalWrite(byte* buffer, uint count) {
		var written = 0;
		NativeFile.Write(_handle, buffer, count, ref written);
	}

	public override long Seek(long offset, SeekOrigin origin) {
		long mungedOffset = offset;
		CheckDisposed();
		if (origin == SeekOrigin.Current) throw new NotImplementedException("only supports seek origin begin/end");
		if (origin == SeekOrigin.End) mungedOffset = Length + offset;
		var aligned = GetLowestAlignment(mungedOffset);
		var left = (int)(mungedOffset - aligned);
		Flush();
		_bufferedCount = left;
		_aligned = aligned == left;
		_lastPosition = aligned;
		//TODO cant do two seeks + a read here.
		SeekInternal(aligned, SeekOrigin.Begin);
		_needsRead = true;
		return offset;
	}

	private long GetLowestAlignment(long offset) {
		return offset - (offset % _blockSize);
	}

	public override void SetLength(long value) {
		CheckDisposed();
		var aligned = GetLowestAlignment(value);
		aligned = aligned == value ? aligned : aligned + _blockSize;
		NativeFile.SetFileSize(_handle, aligned);
		if(Position > aligned)
			Seek(aligned, SeekOrigin.Begin);
	}

	public override int Read(byte[] buffer, int offset, int count) {
		CheckDisposed();
		if (offset < 0 || buffer.Length < offset) throw new ArgumentException("offset");
		if (count < 0 || buffer.Length < count) throw new ArgumentException("offset");
		if (offset + count > buffer.Length)
			throw new ArgumentException("offset + count must be less than size of array");
		var position = GetLowestAlignment(Position);
		var roffset = (int)(Position - position);

		var bytesRead = _readBufferSize;
		if (_readLocation + _readBufferSize <= position || _readLocation > position || _readLocation == -1) {
			SeekInternal(position, SeekOrigin.Begin);
			bytesRead = NativeFile.Read(_handle, _readBuffer, 0, _readBufferSize);
			_readLocation = position;
		} else if (_readLocation != position) {
			roffset += (int)(position - _readLocation);
		}

		var bytesAvailable = bytesRead - roffset;
		if (bytesAvailable <= 0) return 0;
		var toCopy = count > bytesAvailable ? bytesAvailable : count;

		MemCopy(_readBuffer, roffset, buffer, offset, toCopy);
		_bufferedCount += toCopy;
		if (count - toCopy == 0) return toCopy;
		return toCopy + Read(buffer, offset + toCopy, count - toCopy);
	}

	public override void Write(byte[] buffer, int offset, int count) {
		CheckDisposed();
		var done = false;
		long left = count;
		long current = offset;
		if (_needsRead) {
			SeekInternal(_lastPosition, SeekOrigin.Begin);
			NativeFile.Read(_handle, _writeBuffer, 0, (int)_blockSize);
			SeekInternal(_lastPosition, SeekOrigin.Begin);
			_needsRead = false;
		}

		while (!done) {
			_needsFlush = true;
			if (_bufferedCount + left < _writeBufferSize) {
				CopyBuffer(buffer, current, left);
				done = true;
				current += left;
			} else {
				var toFill = _writeBufferSize - _bufferedCount;
				CopyBuffer(buffer, current, toFill);
				Flush();
				left -= toFill;
				current += toFill;
				done = left == 0;
			}
		}
	}

	private void CopyBuffer(byte[] buffer, long offset, long count) {
		MemCopy(buffer, offset, _writeBuffer, _bufferedCount, count);
		_bufferedCount += count;
	}

	public override bool CanRead {
		get {
			CheckDisposed();
			return true;
		}
	}

	public override bool CanSeek {
		get {
			CheckDisposed();
			return true;
		}
	}

	public override bool CanWrite {
		get {
			CheckDisposed();
			return true;
		}
	}

	public override long Length {
		get {
			CheckDisposed();
			return NativeFile.GetFileSize(_handle);
		}
	}

	public override long Position {
		get {
			CheckDisposed();
			if (_aligned)
				return _lastPosition + _bufferedCount;
			return GetLowestAlignment(_lastPosition) + _bufferedCount;
		}
		set {
			CheckDisposed();
			Seek(value, SeekOrigin.Begin);
		}
	}

	private void SetBuffer(long alignedbuffer, long left) {
		MemCopy(_writeBuffer, alignedbuffer, _writeBuffer, 0, left);
	}

	[System.Diagnostics.Conditional("DEBUG")]
	private void CheckDisposed() {
		//only check in debug
		if (_handle == null) throw new ObjectDisposedException("object is disposed.");
	}

	protected override void Dispose(bool disposing) {
		if (_handle == null) return;
		Flush();
		_handle.Close();
		_handle = null;
		_readBuffer = (byte*)IntPtr.Zero;
		_writeBuffer = (byte*)IntPtr.Zero;
		Marshal.FreeHGlobal(_readBufferOriginal);
		Marshal.FreeHGlobal(_writeBufferOriginal);
		GC.SuppressFinalize(this);
	}
}
