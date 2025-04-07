using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using EventStore.Common.Log;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;

namespace EventStore.Projections.Core.Tests.Playground {
	public class LaunchpadBase {
		protected readonly ILogger _logger = LogManager.GetLoggerFor<LaunchpadBase>();

		protected readonly Func<string, string, IDictionary<string, string>, IDisposable> _launch = StartHelperProcess;

		private class DisposeHandler : IDisposable {
			private readonly Action _dispose;

			public DisposeHandler(Action dispose) {
				_dispose = dispose;
			}

			public void Dispose() {
				_dispose();
			}
		}

		private static IDisposable StartHelperProcess(
			string executable, string commandLine, IDictionary<string, string> additionalEnvironment) {
			foreach (var v in additionalEnvironment) {
				Environment.SetEnvironmentVariable(v.Key, v.Value);
			}

			var appDomain = AppDomain.CreateDomain(
				Path.GetFileNameWithoutExtension(executable));
			ThreadPool.QueueUserWorkItem(
				state => {
					try {
						var result = appDomain.ExecuteAssembly(executable, commandLine.Split(' '));
						Console.WriteLine(executable + "has exited with code " + result);
					} catch (AppDomainUnloadedException) {
					}
				});
			return new DisposeHandler(() => AppDomain.Unload(appDomain));
		}

		private static IDisposable RestartProcess(
			string executable, string commandLine, IDictionary<string, string> additionalEnvironment) {
			var existing = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
			foreach (var p in existing) {
				Console.WriteLine("Killing process {0} : {1}", p.Id, p.ProcessName);
				p.Kill();
			}

			var startInfo = new ProcessStartInfo {
				UseShellExecute = false,
				Arguments = commandLine,
				FileName = executable,
				//

				//                    CreateNoWindow = true,
				//                    RedirectStandardError = true,
				//                    RedirectStandardOutput = true,
			};
			foreach (var pair in additionalEnvironment) {
				startInfo.EnvironmentVariables.Add(pair.Key, pair.Value);
			}

			var process = Process.Start(startInfo);
			return process;
		}

		protected class DumpingHandler : IHandle<Message> {
			public void Handle(Message message) {
				Console.WriteLine("Received: {0}({1})", message.GetType(), message);
			}
		}
	}
}
