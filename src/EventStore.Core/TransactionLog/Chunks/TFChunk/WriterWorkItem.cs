using System;
using System.IO;
using System.Security.Cryptography;
using EventStore.Common.Utils;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk {
	internal class WriterWorkItem {
		public Stream WorkingStream {
			get { return _workingStream; }
		}

		public long StreamLength {
			get { return _workingStream.Length; }
		}

		public long StreamPosition {
			get { return _workingStream.Position; }
		}

		private readonly Stream _fileStream;
		private UnmanagedMemoryStream _memStream;
		private Stream _workingStream;

		public readonly MemoryStream Buffer;
		public readonly BinaryWriter BufferWriter;
		public readonly HashAlgorithm MD5;

		public WriterWorkItem(Stream fileStream, UnmanagedMemoryStream memStream, HashAlgorithm md5) {
			_fileStream = fileStream;
			_memStream = memStream;
			_workingStream = (Stream)fileStream ?? memStream;
			Buffer = new MemoryStream(8192);
			BufferWriter = new BinaryWriter(Buffer);
			MD5 = md5;
		}

		public void SetMemStream(UnmanagedMemoryStream memStream) {
			_memStream = memStream;
			if (_fileStream == null)
				_workingStream = memStream;
		}

		public void AppendData(byte[] buf, int offset, int len) {
			// as we are always append-only, stream's position should be right here
			if (_fileStream != null)
				_fileStream.Write(buf, 0, len);
			//MEMORY
			var memStream = _memStream;
			if (memStream != null)
				memStream.Write(buf, 0, len);
		}

		public void ResizeStream(int fileSize) {
			if (_fileStream != null)
				_fileStream.SetLength(fileSize);
			var memStream = _memStream;
			if (memStream != null)
				memStream.SetLength(fileSize);
		}

		public void Dispose() {
			if (_fileStream != null)
				_fileStream.Dispose();

			DisposeMemStream();
		}

		public void FlushToDisk() {
			if (_fileStream == null)
				return;
			var fs = _fileStream as FileStream;
			if (fs != null)
				fs.FlushToDisk(); //because of broken flush in filestream in 3.5
			else {
				_fileStream.Flush();
			}
		}

		public void DisposeMemStream() {
			var memStream = _memStream;
			if (memStream != null) {
				memStream.Dispose();
				_memStream = null;
			}
		}

		public void EnsureMemStreamLength(long length) {
			throw new NotSupportedException("Scavenging with in-mem DB is not supported.");
		}
	}
}
