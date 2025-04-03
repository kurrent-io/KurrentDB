// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Text;
using EventStore.Core.TransactionLog.Unbuffered;
using Xunit;

namespace EventStore.SystemRuntime.Tests;

public sealed class ProcessStatsTests : IDisposable {
	private readonly DirectoryInfo _directory;

	public ProcessStatsTests() {
		string directoryPath = Path.Combine(Path.GetTempPath(), string.Format("ESX-{0}-{1}", Guid.NewGuid(), nameof(ProcessStatsTests)));
		_directory = Directory.CreateDirectory(directoryPath);
		var filePath = Path.Combine(directoryPath, "file.txt");
		WriteAllText(filePath, "the data");
		ReadAllText(filePath);
	}

	private static void ReadAllText(string path) {
		// use UnbufferedFileStream to attempt to skip the OS's file cache
		using var stream =
			UnbufferedFileStream.Create(path, FileMode.Open, FileAccess.Read, FileShare.Read,
				internalWriteBufferSize: 1024, internalReadBufferSize: 1024, writeThrough: false, minBlockSize: 1024);
		var buffer = new byte[8];
		stream.ReadExactly(buffer);
		Assert.Equal("the data", Encoding.UTF8.GetString(buffer));
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
		var diskIo = ProcessStats.GetDiskIo();

		Assert.True(diskIo.ReadBytes > 0);
		Assert.True(diskIo.WrittenBytes > 0);

		if (RuntimeInformation.OsPlatform != RuntimeOSPlatform.OSX) {
			// ops not supported on OSX
			Assert.True(diskIo.ReadOps > 0);
			Assert.True(diskIo.WriteOps > 0);
		}
	}
}
