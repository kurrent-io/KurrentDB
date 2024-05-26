using Microsoft.IO;

namespace EventStore.Streaming;

static class MemoryStreamManager {
	private static readonly RecyclableMemoryStreamManager Manager = new RecyclableMemoryStreamManager();
	
	public static MemoryStream GetStream()                          => Manager.GetStream();
	public static MemoryStream GetStream(byte[] buffer)             => Manager.GetStream(buffer);
	public static MemoryStream GetStream(ReadOnlySpan<byte> buffer) => Manager.GetStream(buffer);
}