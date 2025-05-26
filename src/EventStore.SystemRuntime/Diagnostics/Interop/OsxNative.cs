// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace

using System.Runtime;
using System.Runtime.InteropServices;

namespace System.Diagnostics.Interop;

public static partial class OsxNative {
	public static partial class IO {
		public static DiskIoData GetDiskIo() {
			const int PROC_PID_RUSAGE = 2;
			const int PROC_PID_RUSAGE_SIZE = 232;

			var buffer = Marshal.AllocHGlobal(PROC_PID_RUSAGE_SIZE);
			try {
				if (proc_pidinfo(Environment.ProcessId, PROC_PID_RUSAGE, 0, buffer, PROC_PID_RUSAGE_SIZE) != PROC_PID_RUSAGE_SIZE)
					throw GetLastExternalException($"Failed to get {RuntimeOSPlatform.OSX} usage info to extract disk I/O");

				var info = Marshal.PtrToStructure<rusage_info_v4>(buffer);
				return new(info.ri_diskio_bytesread, info.ri_diskio_byteswritten, 0, 0);
			} finally {
				Marshal.FreeHGlobal(buffer);
			}
		}

		#region . native .

		[LibraryImport("libproc.dylib", SetLastError = true)]
		private static partial int proc_pidinfo(int pid, int flavor, ulong arg, IntPtr buffer, int buffersize);

		[StructLayout(LayoutKind.Sequential)]
		private struct rusage_info_v4 {
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
			public byte[] ri_uuid;

			public ulong ri_user_time;
			public ulong ri_system_time;
			public ulong ri_pkg_idle_wkups;
			public ulong ri_interrupt_wkups;
			public ulong ri_pageins;
			public ulong ri_wired_size;
			public ulong ri_resident_size;
			public ulong ri_phys_footprint;
			public ulong ri_proc_start_abstime;
			public ulong ri_proc_exit_abstime;
			public ulong ri_child_user_time;
			public ulong ri_child_system_time;
			public ulong ri_child_pkg_idle_wkups;
			public ulong ri_child_interrupt_wkups;
			public ulong ri_child_pageins;
			public ulong ri_child_elapsed_abstime;
			public ulong ri_diskio_bytesread;
			public ulong ri_diskio_byteswritten;
			public ulong ri_cpu_time_qos_default;
			public ulong ri_cpu_time_qos_maintenance;
			public ulong ri_cpu_time_qos_background;
			public ulong ri_cpu_time_qos_utility;
			public ulong ri_cpu_time_qos_legacy;
			public ulong ri_cpu_time_qos_user_initiated;
			public ulong ri_cpu_time_qos_user_interactive;
			public ulong ri_billed_system_time;
			public ulong ri_serviced_system_time;
			public ulong ri_logical_writes;
		}

		#endregion
	}

	public static partial class Memory {
		public static long GetFreeMemory() {
			const int HOST_VM_INFO64 = 4;
			const int KERN_SUCCESS = 0;

			var host = mach_host_self();

			if (host_page_size(host, out var page_size) != KERN_SUCCESS)
				throw GetLastExternalException($"Failed to get {RuntimeOSPlatform.OSX} host page size to calculate free memory");

			var count = (uint)Marshal.SizeOf<vm_statistics64>() / sizeof(uint);
			if (host_statistics64(host, HOST_VM_INFO64, out var vmStats, ref count) != KERN_SUCCESS)
				throw GetLastExternalException($"Failed to get {RuntimeOSPlatform.OSX} host statistics to calculate free memory");

			return (long)vmStats.free_count * page_size;
		}

		#region . native .

		[LibraryImport("libSystem.dylib", SetLastError = true)]
		private static partial IntPtr mach_host_self();

		[LibraryImport("libSystem.dylib", SetLastError = true)]
		private static partial int host_page_size(IntPtr host, out uint page_size);

		[LibraryImport("libSystem.dylib", SetLastError = true)]
		private static partial int host_statistics64(IntPtr host, int flavor, out vm_statistics64 vmStats, ref uint count);

		[StructLayout(LayoutKind.Sequential)]
		private struct vm_statistics64 {
			public uint free_count;                             // # of pages free
			public uint active_count;                           // # of pages active
			public uint inactive_count;                         // # of pages inactive
			public uint wire_count;                             // # of pages wired down
			public uint zero_fill_count;                        // # of zero fill pages
			public uint reactivations;                          // # of pages reactivated
			public uint pageins;                                // # of pageins
			public uint pageouts;                               // # of pageouts
			public uint faults;                                 // # of faults
			public uint cow_faults;                             // # of copy-on-writes
			public uint lookups;                                // object cache lookups
			public uint hits;                                   // object cache hits
			public uint purges;                                 // # of pages purged
			public uint purgeable_count;                        // # of pages purgeable
			public uint speculative_count;                      // # of pages speculative
			public ulong decompressions;                         // # of pages decompressed
			public ulong compressions;                           // # of pages compressed
			public ulong swapins;                                // # of pages swapped in (via compression segments)
			public ulong swapouts;                               // # of pages swapped out (via compression segments)
			public uint compressor_page_count;                  // # of pages used by the compressed pager to hold all the compressed data
			public uint throttled_count;                        // # of pages throttled
			public uint external_page_count;                    // # of pages that are file-backed (non-swap)
			public uint internal_page_count;                    // # of pages that are anonymous
			public uint total_uncompressed_pages_in_compressor; // # of pages (uncompressed) held within the compressor.
		}

		#endregion
	}

	static ExternalException GetLastExternalException(string errorMessage) {
		var errorCode = Marshal.GetLastPInvokeError();
		return errorCode != 0
			? new ExternalException($"{errorMessage}. P/Invoke error code: {errorCode}. Message: {Marshal.GetLastPInvokeErrorMessage()}")
			: new ExternalException(errorMessage);
	}
}
