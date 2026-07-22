// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Text;
using Mono.Unix;
using Mono.Unix.Native;
using Xunit;

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

		var stats = ProcessStats.GetDiskIo();

		Assert.True(stats.ReadBytes >= 8UL);
		Assert.True(stats.WrittenBytes >= 8UL);

		if (!RuntimeInformation.IsOSX) {
			// ops not supported on OSX
			Assert.True(stats.ReadOps  > 0);
			Assert.True(stats.WriteOps > 0);
		}
	}

	private static string ReadAllText(string filePath) {
		using var handle = File.OpenHandle(filePath);

		if (RuntimeInformation.IsUnix) {
			// reset cache for this file, so the OS really reads it from disk
			int r;
			do {
				r = Syscall.posix_fadvise(handle.DangerousGetHandle().ToInt32(), 0, 0, PosixFadviseAdvice.POSIX_FADV_DONTNEED);
			} while (UnixMarshal.ShouldRetrySyscall(r));
			UnixMarshal.ThrowExceptionForLastErrorIf(r);
		}

		Span<byte> buffer = stackalloc byte[128];
		var read = RandomAccess.Read(handle, buffer, 0);
		return Encoding.UTF8.GetString(buffer[..read]);
	}
}
