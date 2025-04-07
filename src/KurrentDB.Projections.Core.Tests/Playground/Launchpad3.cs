using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using EventStore.Common.Utils;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Playground {
	[TestFixture, Explicit, Category("Manual")]
	public class Launchpad3 : LaunchpadBase {
		private IDisposable _vnodeProcess;
		private readonly IDisposable _clientProcess = null;

		private string _binFolder;
		private Dictionary<string, string> _environment;
		private string _dbPath;

		[SetUp]
		public void Setup() {
			if (!OS.IsUnix)
				AllocConsole(); // this is required to keep console open after ExecuteAssembly has exited

			_binFolder = AppDomain.CurrentDomain.BaseDirectory;
			_dbPath = Path.GetFullPath(Path.Combine(_binFolder, @"..\..\..\..\Data"));
			_environment = new Dictionary<string, string> {{"EVENTSTORE_LOGSDIR", _dbPath}};

			var vnodeExecutable = Path.Combine(_binFolder, @"EventStore.Projections.Worker.exe");


			string vnodeCommandLine =
				string.Format(@"--ip=127.0.0.1 --db={0} --stats-frequency-sec=10 -t1111 --http-port=2111", _dbPath);

			_vnodeProcess = _launch(vnodeExecutable, vnodeCommandLine, _environment);
		}

		[TearDown]
		public void Teardown() {
			if (_vnodeProcess != null) _vnodeProcess.Dispose();
			if (_clientProcess != null) _clientProcess.Dispose();
		}

		[Test, Explicit]
		public void RunSingle() {
			Thread.Sleep(1000);
			var timer = Stopwatch.StartNew();
			while (timer.Elapsed.TotalSeconds < 15) {
				var request = WebRequest.Create(@"http://127.0.0.1:2111/projections/onetime");
				try {
					request.Method = "POST";
					request.Timeout = 5000;
					var query = File.ReadAllText(Path.Combine(_binFolder, @"Queries\1Query.js"));
					var data = Helper.UTF8NoBom.GetBytes(query);
					using (var requestStream = request.GetRequestStream())
						requestStream.Write(data, 0, data.Length);
					var response = (HttpWebResponse)request.GetResponse();
					Console.WriteLine(response.StatusDescription);
					Console.WriteLine(response.GetResponseHeader("Location"));
					Console.WriteLine(response.GetResponseHeader("Z"));
				} catch (SocketException) {
					continue;
				} catch (TimeoutException) {
					continue;
				} catch (WebException) {
					continue;
				}

				break;
			}

			timer.Stop();
			Thread.Sleep(100000);
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool AllocConsole();
	}
}
