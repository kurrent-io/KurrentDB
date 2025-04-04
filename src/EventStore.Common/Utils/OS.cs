using System;
using System.Reflection;
using ILogger = Serilog.ILogger;

namespace EventStore.Common.Utils {
	public enum OsFlavor {
		Unknown,
		Windows,
		Linux,
		BSD,
		MacOS
	}

	public class OS {
		private static readonly ILogger Log = Serilog.Log.ForContext<OS>();

		public static readonly OsFlavor OsFlavor = DetermineOSFlavor();

		public static bool IsUnix {
			get {
				var platform = (int)Environment.OSVersion.Platform;
				return (platform == 4) || (platform == 6) || (platform == 128);
			}
		}

		public static string GetHomeFolder() {
			return IsUnix
				? Environment.GetEnvironmentVariable("HOME")
				: Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
		}

		public static string GetRuntimeVersion() {
			var informationalVersion = typeof(object).Assembly
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

			if (informationalVersion == null) {
				return "Unknown";
			}

			var separatorIndex = informationalVersion.IndexOf('+');

			return ".NET " + (separatorIndex == -1
				? informationalVersion
				: informationalVersion[..separatorIndex] + "/" + informationalVersion.Substring(separatorIndex + 1, 9));
		}

		private static OsFlavor DetermineOSFlavor() {
			if (!IsUnix) // assume Windows
				return OsFlavor.Windows;

			string uname = null;
			try {
				uname = ShellExecutor.GetOutput("uname", "");
			} catch (Exception ex) {
				Log.Error(ex, "Couldn't determine the flavor of Unix-like OS.");
			}

			uname = uname.Trim().ToLower();
			switch (uname) {
				case "linux":
					return OsFlavor.Linux;
				case "darwin":
					return OsFlavor.MacOS;
				case "freebsd":
				case "netbsd":
				case "openbsd":
					return OsFlavor.BSD;
				default:
					return OsFlavor.Unknown;
			}
		}
	}
}
