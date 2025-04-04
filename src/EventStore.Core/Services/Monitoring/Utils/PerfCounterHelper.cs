using System;
using System.Diagnostics;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Monitoring.Utils {
	internal class PerfCounterHelper : IDisposable {
		private const int InvalidCounterResult = -1;

		private readonly Serilog.ILogger _log;

		private readonly PerformanceCounter _totalCpuCounter;
		private readonly PerformanceCounter _totalMemCounter; //doesn't work on mono

		public PerfCounterHelper(ILogger log) {
			_log = log;

			_totalCpuCounter = CreatePerfCounter("Processor", "% Processor Time", "_Total");
			_totalMemCounter = CreatePerfCounter("Memory", "Available Bytes");
		}

		private PerformanceCounter CreatePerfCounter(string category, string counter, string instance = null) {
			if (!OperatingSystem.IsWindows()) {
				return null;
			}

			try {
				return string.IsNullOrEmpty(instance)
					? new PerformanceCounter(category, counter)
					: new PerformanceCounter(category, counter, instance);
			} catch (Exception ex) {
				_log.Debug(
					"Could not create performance counter: category='{category}', counter='{counter}', instance='{instance}'. Error: {e}",
					category, counter, instance ?? string.Empty, ex.Message);
				return null;
			}
		}

		///<summary>
		///Total CPU usage in percentage
		///</summary>
		public float GetTotalCpuUsage() {
			Debug.Assert(OperatingSystem.IsWindows());
			return _totalCpuCounter?.NextValue() ?? InvalidCounterResult;
		}

		///<summary>
		///Free memory in bytes
		///</summary>
		public long GetFreeMemory() {
			Debug.Assert(OperatingSystem.IsWindows());
			return _totalMemCounter?.NextSample().RawValue ?? InvalidCounterResult;
		}

		public void Dispose() {
			_totalCpuCounter?.Dispose();
			_totalMemCounter?.Dispose();
		}
	}
}
