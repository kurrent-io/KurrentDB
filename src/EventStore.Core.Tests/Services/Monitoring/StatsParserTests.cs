using System;
using EventStore.Core.Services.Monitoring.Stats;
using NUnit.Framework;
using Serilog;

namespace EventStore.Core.Tests.Services.Monitoring {
	[TestFixture]
	public class IoParserTests {
		private readonly string ioStr = "rchar: 23550615" + Environment.NewLine +
										"wchar: 290654" + Environment.NewLine +
										"syscr: 184391" + Environment.NewLine +
										"syscw: 3273" + Environment.NewLine +
										"read_bytes: 13824000" + Environment.NewLine +
										"write_bytes: 188416" + Environment.NewLine +
										"cancelled_write_bytes: 0" + Environment.NewLine;

		[Test]
		public void sample_io_doesnt_crash() {
			var io = DiskIo.ParseOnUnix(ioStr, Log.Logger);
			var success = io != null;

			Assert.That(success, Is.True);
		}

		[Test]
		public void bad_io_crashes() {
			var badIoStr = ioStr.Remove(5, 20);

			DiskIo io = DiskIo.ParseOnUnix(badIoStr, Log.Logger);
			var success = io != null;

			Assert.That(success, Is.False);
		}

		[Test]
		public void read_bytes_parses_ok() {
			var io = DiskIo.ParseOnUnix(ioStr, Log.Logger);

			Assert.That(io.ReadBytes, Is.EqualTo(13824000));
		}

		[Test]
		public void write_bytes_parses_ok() {
			var io = DiskIo.ParseOnUnix(ioStr, Log.Logger);

			Assert.That(io.WrittenBytes, Is.EqualTo(188416));
		}
	}
}
