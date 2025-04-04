using System;

namespace EventStore.Core.Tests.Certificates {
	public static class ByteExtensions {
		public static string PEM(this byte[] bytes, string label) {
			return $"-----BEGIN {label}-----\n" + Convert.ToBase64String(bytes) + "\n" + $"-----END {label}-----\n";
		}
	}
}
