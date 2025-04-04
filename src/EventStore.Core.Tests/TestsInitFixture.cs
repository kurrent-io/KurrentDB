using System;
using System.Net;
using System.Runtime.InteropServices;
using EventStore.Common.Utils;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests {
	[SetUpFixture]
	public class TestsInitFixture {
		[OneTimeSetUp]
		public void SetUp() {
			ServicePointManager.DefaultConnectionLimit = 1000;
			LogEnvironmentInfo();
		}

		private void LogEnvironmentInfo() {
			var log = Serilog.Log.ForContext<TestsInitFixture>();

			log.Information("\n{0,-25} {1} ({2}/{3}, {4})\n"
					 + "{5,-25} {6} ({7})\n"
					 + "{8,-25} {9} ({10}-bit)\n"
					 + "{11,-25} {12}\n\n",
				"ES VERSION:", VersionInfo.Version, VersionInfo.Tag, VersionInfo.Hashtag, VersionInfo.Timestamp,
				"OS:", OS.OsFlavor, Environment.OSVersion,
				"RUNTIME:", OS.GetRuntimeVersion(), Marshal.SizeOf(typeof(IntPtr)) * 8,
				"GC:",
				GC.MaxGeneration == 0
					? "NON-GENERATION (PROBABLY BOEHM)"
					: string.Format("{0} GENERATIONS", GC.MaxGeneration + 1));
		}

		[OneTimeTearDown]
		public void TearDown() {
			var runCount = Math.Max(1, MiniNode<LogFormat.V2, string>.RunCount);
			var msg = string.Format("Total running time of MiniNode (Log V2): {0} (mean {1})\n" +
									"Total starting time of MiniNode (Log V2): {2} (mean {3})\n" +
									"Total stopping time of MiniNode (Log V2): {4} (mean {5})\n" +
									"Total run count (Log V2): {6}",
				MiniNode<LogFormat.V2, string>.RunningTime.Elapsed, TimeSpan.FromTicks(MiniNode<LogFormat.V2, string>.RunningTime.Elapsed.Ticks / runCount),
				MiniNode<LogFormat.V2, string>.StartingTime.Elapsed, TimeSpan.FromTicks(MiniNode<LogFormat.V2, string>.StartingTime.Elapsed.Ticks / runCount),
				MiniNode<LogFormat.V2, string>.StoppingTime.Elapsed, TimeSpan.FromTicks(MiniNode<LogFormat.V2, string>.StoppingTime.Elapsed.Ticks / runCount),
				MiniNode<LogFormat.V2, string>.RunCount);

			Console.WriteLine(msg);
			Console.WriteLine();

			runCount = Math.Max(1, MiniNode<LogFormat.V3, long>.RunCount);
			msg = string.Format("Total running time of MiniNode (Log V3): {0} (mean {1})\n" +
									"Total starting time of MiniNode (Log V3): {2} (mean {3})\n" +
									"Total stopping time of MiniNode (Log V3): {4} (mean {5})\n" +
									"Total run count (Log V3): {6}",
				MiniNode<LogFormat.V3, long>.RunningTime.Elapsed, TimeSpan.FromTicks(MiniNode<LogFormat.V3, long>.RunningTime.Elapsed.Ticks / runCount),
				MiniNode<LogFormat.V3, long>.StartingTime.Elapsed, TimeSpan.FromTicks(MiniNode<LogFormat.V3, long>.StartingTime.Elapsed.Ticks / runCount),
				MiniNode<LogFormat.V3, long>.StoppingTime.Elapsed, TimeSpan.FromTicks(MiniNode<LogFormat.V3, long>.StoppingTime.Elapsed.Ticks / runCount),
				MiniNode<LogFormat.V3, long>.RunCount);

			Console.WriteLine(msg);
		}
	}
}
