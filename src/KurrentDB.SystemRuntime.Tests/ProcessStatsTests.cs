// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Mono.Unix;
using Mono.Unix.Native;
using Xunit;
using RuntimeInformation = System.Runtime.RuntimeInformation;

namespace KurrentDB.SystemRuntime.Tests;

public sealed class ProcessStatsTests : IDisposable {
	private readonly DirectoryInfo _directory;
	private readonly string _filePath;

	public ProcessStatsTests() {
		string directoryPath = Path.Combine(Path.GetTempPath(), $"ESX-{Guid.NewGuid()}-{nameof(ProcessStatsTests)}");
		_directory = Directory.CreateDirectory(directoryPath);
		_filePath = Path.Combine(directoryPath, "file.txt");
	}

	private static void WriteAllText(string path, string data) {
		using var handle = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.Read, FileOptions.WriteThrough);
		RandomAccess.Write(handle, Encoding.UTF8.GetBytes(data), 0L);
		RandomAccess.FlushToDisk(handle);
	}

	public void Dispose() {
		_directory.Delete(recursive: true);
	}

	[Fact]
	public void TestProcessStats() {
		WriteAllText(_filePath, "the data");

		var data = ReadAllText(_filePath);
		Assert.Equal("the data", data);

		var diskIo = ProcessStats.GetDiskIo();

		Assert.True(diskIo.ReadBytes >= 8UL);
		Assert.True(diskIo.WrittenBytes >= 8UL);

		if (!RuntimeInformation.IsOSX) {
			// ops not supported on OSX
			Assert.True(diskIo.ReadOps  > 0);
			Assert.True(diskIo.WriteOps > 0);
		}
	}

	private static string ReadAllText(string filePath) {
		if (RuntimeInformation.IsUnix) {
			return ReadAllTextDirect(filePath);
		}

		// Windows: read through the managed file handle.
		using var handle = File.OpenHandle(filePath);
		Span<byte> buffer = stackalloc byte[128];
		var read = RandomAccess.Read(handle, buffer, 0);
		return Encoding.UTF8.GetString(buffer[..read]);
	}

	// Unix: open the file with O_DIRECT so the read bypasses the page cache and
	// really hits the disk, which is what ProcessStats.GetDiskIo() needs to observe.
	private static unsafe string ReadAllTextDirect(string filePath) {
		var fd = Checked(() => Syscall.open(filePath, OpenFlags.O_RDONLY | OpenFlags.O_DIRECT));

		Stat stat = default;
		Checked(() => Syscall.fstat(fd, out stat));

		var blockSize = (nuint)stat.st_blksize;
		var buffer = NativeMemory.AlignedAlloc(Math.Max(blockSize, 128), blockSize);

		try {
			var read = Checked(() => Syscall.pread(fd, buffer, blockSize, 0));
			return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(buffer, (int)read));
		} finally {
			NativeMemory.AlignedFree(buffer);
			Checked(() => Syscall.close(fd));
		}

		static T Checked<T>(Func<T> syscall)  where T: ISignedNumber<T> {
			T ret;
			do {
				ret = syscall();
			} while (UnixMarshal.ShouldRetrySyscall(int.CreateTruncating(ret)));
			UnixMarshal.ThrowExceptionForLastErrorIf(int.CreateTruncating(ret));
			return ret;
		}
	}
}
