// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace EventStore.Core.TransactionLog.Unbuffered;

public unsafe class NativeFileWindows : INativeFile {
	public uint GetDriveSectorSize(string path) {
		WinNative.GetDiskFreeSpace(Path.GetPathRoot(path), out _, out var size, out _, out _);
		return size;
	}

	public long GetPageSize(string path) {
		return GetDriveSectorSize(path);
	}

	public void SetFileSize(SafeFileHandle handle, long count) {
		var low = (int)(count & 0xffffffff);
		var high = (int)(count >> 32);
		WinNative.SetFilePointer(handle, low, out high, WinNative.EMoveMethod.Begin);
		if (!WinNative.SetEndOfFile(handle)) {
			throw new Win32Exception();
		}

		FSync(handle);
	}

	private static void FSync(SafeFileHandle handle) {
		WinNative.FlushFileBuffers(handle);
	}

	public void Write(SafeFileHandle handle, byte* buffer, uint count, ref int written) {
		if (!WinNative.WriteFile(handle, buffer, count, ref written, IntPtr.Zero)) {
			throw new Win32Exception();
		}
	}

	public int Read(SafeFileHandle handle, byte* buffer, int offset, int count) {
		var read = 0;

		if (!WinNative.ReadFile(handle, buffer, count, ref read, 0)) {
			throw new Win32Exception();
		}

		return read;
	}

	public long GetFileSize(SafeFileHandle handle) {
		if (!WinNative.GetFileSizeEx(handle, out var size)) {
			throw new Win32Exception();
		}

		return size;
	}

	//TODO UNBUFF use FileAccess etc or do custom?
	public SafeFileHandle Create(string path, FileAccess acc, FileShare readWrite, FileMode mode, int flags) {
		var handle = WinNative.CreateFile(path,
			acc,
			FileShare.ReadWrite,
			IntPtr.Zero,
			mode,
			flags,
			IntPtr.Zero);
		if (handle.IsInvalid) {
			throw new Win32Exception();
		}

		return handle;
	}


	public SafeFileHandle CreateUnbufferedRW(string path, FileAccess acc, FileShare share, FileMode mode,
		bool writeThrough) {
		var flags = ExtendedFileOptions.NoBuffering;
		if (writeThrough) flags = flags | ExtendedFileOptions.WriteThrough;
		var handle = WinNative.CreateFile(path,
			acc,
			share,
			IntPtr.Zero,
			FileMode.OpenOrCreate,
			(int)flags,
			IntPtr.Zero);
		if (handle.IsInvalid) {
			throw new Win32Exception();
		}

		return handle;
	}

	public void Seek(SafeFileHandle handle, long position, SeekOrigin origin) {
		var low = (int)(position & 0xffffffff);
		var high = (int)(position >> 32);
		var f = WinNative.SetFilePointer(handle, low, out high, WinNative.EMoveMethod.Begin);
		if (f == WinNative.INVALID_SET_FILE_POINTER) {
			throw new Win32Exception();
		}
	}
}
