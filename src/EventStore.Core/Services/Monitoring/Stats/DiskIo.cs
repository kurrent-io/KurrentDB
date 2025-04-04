using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using EventStore.Common.Utils;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Monitoring.Stats {
	public class DiskIo {
		private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<DiskIo>();

		///<summary>
		///The number of bytes read by EventStore since start.
		///</summary>
		public readonly ulong ReadBytes;

		///<summary>
		///The number of bytes written by EventStore since start.
		///</summary>
		public readonly ulong WrittenBytes;

		///<summary>
		///The number of read operations by EventStore since start.
		///</summary>
		public readonly ulong ReadOps;

		///<summary>
		///The number of write operations by EventStore since start.
		///</summary>
		public readonly ulong WriteOps;

		public DiskIo(ulong bytesRead, ulong bytesWritten, ulong readOps, ulong writeOps) {
			ReadBytes = bytesRead;
			WrittenBytes = bytesWritten;
			ReadOps = readOps;
			WriteOps = writeOps;
		}

		public static DiskIo GetDiskIo(int procId, Serilog.ILogger logger) {
			try {
				return OS.IsUnix ? GetOnUnix(procId, logger) : GetOnWindows(logger);
			} catch (Exception exc) {
				Log.Debug("Getting disk IO error: {e}.", exc.Message);
				return null;
			}
		}

		// http://stackoverflow.com/questions/3633286/understanding-the-counters-in-proc-pid-io
		private static DiskIo GetOnUnix(int procId, Serilog.ILogger log) {
			var procIoFile = string.Format("/proc/{0}/io", procId);
			if (!File.Exists(procIoFile)) // if no procfs exists/is mounted -- just don't return stats
				return null;
			var procIoStr = File.ReadAllText(procIoFile);
			return ParseOnUnix(procIoStr, log);
		}

		public static DiskIo ParseOnUnix(string procIoStr, Serilog.ILogger log) {
			ulong readBytes, writtenBytes, readOps, writeOps;
			try {
				var dict = procIoStr.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
					.Select(x => x.Split(':'))
					.ToDictionary(s => s[0].Trim(), s => s[1].Trim());
				readBytes = ulong.Parse(dict["read_bytes"]);
				writtenBytes = ulong.Parse(dict["write_bytes"]);
				readOps = ulong.Parse(dict["syscr"]);
				writeOps = ulong.Parse(dict["syscw"]);
			} catch (Exception ex) {
				log.Information(ex, "Could not parse Linux stats.");
				return null;
			}

			return new DiskIo(readBytes, writtenBytes, readOps, writeOps);
		}

		private static DiskIo GetOnWindows(ILogger log) {
			Ensure.NotNull(log, "log");

			IO_COUNTERS counters;
			Process proc = null;
			try {
				proc = Process.GetCurrentProcess();
				GetProcessIoCounters(proc.Handle, out counters);
			} catch (Exception ex) {
				log.Information(ex, "Error while reading disk IO on Windows.");
				return null;
			} finally {
				if (proc != null)
					proc.Dispose();
			}

			return new DiskIo(counters.ReadTransferCount, counters.WriteTransferCount,
				counters.ReadOperationCount, counters.WriteOperationCount);
		}

#pragma warning disable 649
#pragma warning disable 169

		// http://msdn.microsoft.com/en-us/library/ms683218%28VS.85%29.aspx
		private struct IO_COUNTERS {
			public ulong ReadOperationCount;
			public ulong WriteOperationCount;
			public ulong OtherOperationCount;
			public ulong ReadTransferCount;
			public ulong WriteTransferCount;
			public ulong OtherTransferCount;
		}
#pragma warning restore 649
#pragma warning restore 169

		[DllImport("kernel32.dll")]
		static extern bool GetProcessIoCounters(IntPtr ProcessHandle, out IO_COUNTERS IoCounters);
	}
}
