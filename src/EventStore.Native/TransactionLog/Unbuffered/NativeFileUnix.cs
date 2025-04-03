// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Win32.SafeHandles;
using Mono.Unix;
using Mono.Unix.Native;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime;

namespace EventStore.Core.TransactionLog.Unbuffered;

public unsafe class NativeFileUnix : INativeFile {
	public uint GetDriveSectorSize(string path) {
		return 0;
	}

	public long GetPageSize(string path) {
		int r;
		do {
			r = (int)Syscall.sysconf(SysconfName._SC_PAGESIZE);
		} while (UnixMarshal.ShouldRetrySyscall(r));

		UnixMarshal.ThrowExceptionForLastErrorIf(r);
		return r;
	}

	public void SetFileSize(SafeFileHandle handle, long count) {
		int r;
		do {
			r = Syscall.ftruncate(handle.DangerousGetHandle().ToInt32(), count);
		} while (UnixMarshal.ShouldRetrySyscall(r));

		UnixMarshal.ThrowExceptionForLastErrorIf(r);
		FSync(handle);
	}

	private static void FSync(SafeFileHandle handle) {
		Syscall.fsync(handle.DangerousGetHandle().ToInt32());
	}

	public void Write(SafeFileHandle handle, byte* buffer, uint count, ref int written) {
		int ret;
		do {
			ret = (int)Syscall.write(handle.DangerousGetHandle().ToInt32(), buffer, count);
		} while (UnixMarshal.ShouldRetrySyscall(ret));

		if (ret == -1) {
			UnixMarshal.ThrowExceptionForLastErrorIf(ret);
		}

		written = (int)count;
	}

	public int Read(SafeFileHandle handle, byte* buffer, int offset, int count) {
		int r;
		do {
			r = (int)Syscall.read(handle.DangerousGetHandle().ToInt32(), buffer, (ulong)count);
		} while (UnixMarshal.ShouldRetrySyscall(r));

		if (r == -1) {
			UnixMarshal.ThrowExceptionForLastError();
		}

		return count;
	}

	public long GetFileSize(SafeFileHandle handle) {
		Stat s;
		int r;
		do {
			r = Syscall.fstat(handle.DangerousGetHandle().ToInt32(), out s);
		} while (UnixMarshal.ShouldRetrySyscall(r));

		UnixMarshal.ThrowExceptionForLastErrorIf(r);
		return s.st_size;
	}

	//TODO UNBUFF use FileAccess etc or do custom?
	public SafeFileHandle Create(string path, FileAccess acc, FileShare readWrite, FileMode mode, int flags) {
		//TODO convert flags or separate methods?
		return new SafeFileHandle((IntPtr)0, true);
	}


	public SafeFileHandle CreateUnbufferedRW(string path, FileAccess acc, FileShare share, FileMode mode,
		bool writeThrough) {
		//O_RDONLY is 0
		var direct = RuntimeInformation.IsOSX ? OpenFlags.O_RDONLY : OpenFlags.O_DIRECT;
		var flags = GetFlags(acc, mode) | direct;
		var han = Syscall.open(path, flags, FilePermissions.S_IRWXU);
		if (han < 0) {
			throw new Win32Exception();
		}

		var handle = new SafeFileHandle((IntPtr)han, true);
		if (handle.IsInvalid) {
			throw new Exception("Invalid handle");
		}

		MacCaching.Disable(handle);

		return handle;
	}

	private static OpenFlags GetFlags(FileAccess acc, FileMode mode) {
		var flags = OpenFlags.O_RDONLY; //RDONLY is 0
		switch (acc) {
			case FileAccess.Read:
				flags |= OpenFlags.O_RDONLY;
				break;
			case FileAccess.Write:
				flags |= OpenFlags.O_WRONLY;
				break;
			case FileAccess.ReadWrite:
				flags |= OpenFlags.O_RDWR;
				break;
		}

		switch (mode) {
			case FileMode.Append:
				flags |= OpenFlags.O_APPEND;
				break;
			case FileMode.Create:
			case FileMode.CreateNew:
				flags |= OpenFlags.O_CREAT;
				break;
			case FileMode.Truncate:
				flags |= OpenFlags.O_TRUNC;
				break;
		}

		return flags;
	}

	public void Seek(SafeFileHandle handle, long position, SeekOrigin origin) {
		int r;
		do {
			r = (int)Syscall.lseek(handle.DangerousGetHandle().ToInt32(), position, SeekFlags.SEEK_SET);
		} while (UnixMarshal.ShouldRetrySyscall(r));

		UnixMarshal.ThrowExceptionForLastErrorIf(r);
	}
}
