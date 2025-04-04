using System.IO;
using EventStore.Common.Utils;

namespace EventStore.Core.Tests.Http.Users {
	public static class StreamHelpers {
		public static void WriteJson<T>(this Stream stream, T data) {
			var bytes = data.ToJsonBytes();
			stream.Write(bytes, 0, bytes.Length);
		}
	}
}
