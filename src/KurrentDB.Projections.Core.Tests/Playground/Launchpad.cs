using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using EventStore.Common.Utils;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Playground {
	[TestFixture, Explicit, Category("Manual")]
	public class Launchpad : LaunchpadBase {
		private IDisposable _vnodeProcess;
		private IDisposable _managerProcess;
		private IDisposable _clientProcess;
		private IDisposable _projectionsProcess;

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

			var vnodeExecutable = Path.Combine(_binFolder, @"EventStore.ClusterNode.exe");
			var managerExecutable = Path.Combine(_binFolder, @"EventStore.Manager.exe");


			string vnodeCommandLine =
				string.Format(
					@"--ip=127.0.0.1 --db={0} --int-tcp-port=3111 --ext-tcp-port=1111 --http-port=2111 --manager-ip=127.0.0.1 --manager-port=30777 --nodes-count=1 --fake-dns --prepare-count=1 --commit-count=1",
					_dbPath);
			string managerCommandLine = @"--ip=127.0.0.1 --port=30777 --nodes-count=1 --fake-dns";

			_managerProcess = _launch(managerExecutable, managerCommandLine, _environment);
			Thread.Sleep(500);
			_vnodeProcess = _launch(vnodeExecutable, vnodeCommandLine, _environment);
		}

		[TearDown]
		public void Teardown() {
			if (_managerProcess != null) _managerProcess.Dispose();
			if (_vnodeProcess != null) _vnodeProcess.Dispose();
			if (_clientProcess != null) _clientProcess.Dispose();
			if (_projectionsProcess != null) _projectionsProcess.Dispose();
		}

		public void LaunchFlood() {
			var clientExecutable = Path.Combine(_binFolder, @"EventStore.Client.exe");
			string clientCommandLine = @"-i 127.0.0.1 -t 1111 -h 2111 WRFL 1 100";
			_clientProcess = _launch(clientExecutable, clientCommandLine, _environment);
		}

		public void LaunchProjections() {
			var clientExecutable = Path.Combine(_binFolder, @"EventStore.Projections.Worker.exe");
			string clientCommandLine = string.Format(@"--ip 127.0.0.1 -t 1111 -h 2111 --db {0}", _dbPath);
			_projectionsProcess = _launch(clientExecutable, clientCommandLine, _environment);
		}

		[Test]
		public void WriteFloodAndProjections() {
			Thread.Sleep(5000);
			LaunchFlood();
			Thread.Sleep(5000);
			LaunchProjections();
			Thread.Sleep(500);
			LaunchFlood();
			Thread.Sleep(160000);
		}

		[Test]
		public void JustProjections() {
			Thread.Sleep(3500);
			LaunchProjections();
			Thread.Sleep(50000);
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool AllocConsole();
	}
}
