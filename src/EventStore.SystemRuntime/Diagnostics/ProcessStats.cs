// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.Runtime;
using OsxNative = System.Diagnostics.Interop.OsxNative;
using WindowsNative = System.Diagnostics.Interop.WindowsNative;

namespace System.Diagnostics;

[PublicAPI]
public static class ProcessStats {
	public static DiskIoData GetDiskIo() {
		return RuntimeInformation.OsPlatform switch {
			RuntimeOSPlatform.Linux => GetDiskIoLinux(),
			RuntimeOSPlatform.OSX => GetDiskIoOsx(),
			RuntimeOSPlatform.Windows => GetDiskIoWindows(),
			RuntimeOSPlatform.FreeBSD => default,
			_ => throw new NotSupportedException("Operating system not supported")
		};

		static DiskIoData GetDiskIoLinux() {
			const string procIoFile = "/proc/self/io";

			var result = new DiskIoData();
			if (File.Exists(procIoFile)) {
				try {
					foreach (var line in File.ReadLines(procIoFile)) {
						if (TryExtractIoValue(line, "read_bytes", out var readBytes))
							result = result with { ReadBytes = readBytes };
						else if (TryExtractIoValue(line, "write_bytes", out var writeBytes))
							result = result with { WrittenBytes = writeBytes };
						else if (TryExtractIoValue(line, "syscr", out var readOps))
							result = result with { ReadOps = readOps };
						else if (TryExtractIoValue(line, "syscw", out var writeOps)) {
							result = result with { WriteOps = writeOps };
						}

						if (result is {
							    ReadBytes: not 0UL, ReadOps: not 0UL, WriteOps: not 0UL, WrittenBytes: not 0UL
						    })
							break;
					}
				} catch (Exception ex) {
					throw new ApplicationException("Failed to get Linux process I/O info", ex);
				}
			}

			return result;

			static bool TryExtractIoValue(ReadOnlySpan<char> line, ReadOnlySpan<char> key, out ulong value) {
				if (line.StartsWith(key)) {
					var rawValue = line[(key.Length + 1)..].Trim(); // handle the `:` character
					return ulong.TryParse(rawValue, out value);
				}

				value = 0;
				return false;
			}
		}

		static DiskIoData GetDiskIoOsx() =>
			OsxNative.IO.GetDiskIo();

		static DiskIoData GetDiskIoWindows() =>
			WindowsNative.IO.GetDiskIo();
	}
}

/// <summary>
/// Represents a record struct for Disk I/O data.
/// </summary>
public readonly record struct DiskIoData {
	public DiskIoData() { }

	public DiskIoData(ulong readBytes, ulong writtenBytes, ulong readOps, ulong writeOps) {
		ReadBytes = readBytes;
		WrittenBytes = writtenBytes;
		ReadOps = readOps;
		WriteOps = writeOps;
	}

	/// <summary>
	/// Gets or sets the number of bytes read.
	/// </summary>
	public ulong ReadBytes { get; init; }

	/// <summary>
	/// Gets or sets the number of bytes written.
	/// </summary>
	public ulong WrittenBytes { get; init; }

	/// <summary>
	/// Gets or sets the number of read operations.
	/// </summary>
	public ulong ReadOps { get; init; }

	/// <summary>
	/// Gets or sets the number of write operations.
	/// </summary>
	public ulong WriteOps { get; init; }
}
