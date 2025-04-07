using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using EventStore.Common.Utils;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Playground {
	[TestFixture, Explicit, Category("Manual")]
	public class Launchpad2 : LaunchpadBase {
		private IDisposable _vnodeProcess;
		private IDisposable _clientProcess;

		private string _binFolder;
		private Dictionary<string, string> _environment;
		private string _dbPath;

		[SetUp]
		public void Setup() {
			if (!OS.IsUnix)
				AllocConsole(); // this is required to keep console open after executeassemly has exited

			_binFolder = AppDomain.CurrentDomain.BaseDirectory;
			_dbPath = Path.Combine(_binFolder, DateTime.UtcNow.Ticks.ToString());
			_environment = new Dictionary<string, string> {{"EVENTSTORE_LOGSDIR", _dbPath}};

			var vnodeExecutable = Path.Combine(_binFolder, @"EventStore.Projections.Worker.exe");


			string vnodeCommandLine =
				string.Format(
					@"--ip=127.0.0.1 --db={0} --stats-frequency-sec=10 --int-tcp-port=3111 --ext-tcp-port=1111 --http-port=2111",
					_dbPath);

			_vnodeProcess = _launch(vnodeExecutable, vnodeCommandLine, _environment);
		}

		[TearDown]
		public void Teardown() {
			if (_vnodeProcess != null) _vnodeProcess.Dispose();
			if (_clientProcess != null) _clientProcess.Dispose();
		}

		public void LaunchFlood() {
			var clientExecutable = Path.Combine(_binFolder, @"EventStore.Client.exe");
			string clientCommandLine = @"--ip 127.0.0.1 --tcp-port 1111 --http-port 2111 WRFL 1 100";
			_clientProcess = _launch(clientExecutable, clientCommandLine, _environment);
		}

		[Test]
		public void RunSingle() {
			Thread.Sleep(60000);
		}

		[Test]
		public void RunSingleAndFlood() {
			Thread.Sleep(4000);
			LaunchFlood();
			Thread.Sleep(10000);
			LaunchFlood();
			Thread.Sleep(20000);
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool AllocConsole();
	}
}
