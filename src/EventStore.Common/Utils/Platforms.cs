using System;
using System.IO;

namespace EventStore.Common.Utils {
	public class Platforms {
		public static Platform GetPlatform() {
			//http://stackoverflow.com/questions/10138040/how-to-detect-properly-windows-linux-mac-operating-systems
			switch (Environment.OSVersion.Platform) {
				case PlatformID.Unix:
					if (Directory.Exists("/Applications") & Directory.Exists("/System") & Directory.Exists("/Users") &
						Directory.Exists("/Volumes"))
						return Platform.Mac;
					return Platform.Linux;

				case PlatformID.MacOSX:
					return Platform.Mac;

				default:
					return Platform.Windows;
			}
		}
	}

	public enum Platform {
		Windows,
		Linux,
		Mac
	}
}
