// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.Runtime;
using OsxNative = System.Diagnostics.Interop.OsxNative;
using WindowsNative = System.Diagnostics.Interop.WindowsNative;

namespace System.Diagnostics;

[PublicAPI]
public static class ProcessStats {
    public static DiskIoData GetDiskIo(Process process) {
        return RuntimeInformation.OsPlatform switch {
            RuntimeOSPlatform.Linux   => GetDiskIoLinux(process),
            RuntimeOSPlatform.OSX     => GetDiskIoOsx(process),
            RuntimeOSPlatform.Windows => GetDiskIoWindows(process),
            RuntimeOSPlatform.FreeBSD => default,
            _                         => throw new NotSupportedException("Operating system not supported")
        };

        static DiskIoData GetDiskIoLinux(Process process) {
            var procIoFile = $"/proc/{process.Id}/io";

            try {
                var result = new DiskIoData();

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

                    if (result.ReadBytes is not 0 &&
                        result.WrittenBytes is not 0 &&
                        result.ReadOps is not 0 &&
                        result.WriteOps is not 0)
                        break;
                }

                return result;
            }
            catch (Exception ex) {
                throw new ApplicationException("Failed to get Linux process I/O info", ex);
            }

            static bool TryExtractIoValue(string line, string key, out ulong value) {
                if (line.StartsWith(key)) {
                    var rawValue = line[(key.Length + 1)..].Trim(); // handle the `:` character
                    value = Convert.ToUInt64(rawValue);
                    return true;
                }

                value = 0;
                return false;
            }
        }
        
        static DiskIoData GetDiskIoOsx(Process process) => 
            OsxNative.IO.GetDiskIo(process.Id);

        static DiskIoData GetDiskIoWindows(Process process) =>
            WindowsNative.IO.GetDiskIo(process);
    }

    public static DiskIoData GetDiskIo() => 
        GetDiskIo(Process.GetCurrentProcess());
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
