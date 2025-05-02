using System.Collections.Generic;
using EventStore.Core.TransactionLog.Chunks;
using Xunit;

namespace EventStore.Core.XUnit.Tests.TransactionLog.Chunks;

public class PoolingChunkCacheManagerTests {
	readonly FakeInnerManager _recorder = new();
	readonly PoolingChunkCacheManager _sut;

	public PoolingChunkCacheManagerTests() {
		_sut = new(_recorder, minBufferSize: 100, cleanBuffers: false);
	}

	[Fact]
	public void can_allocate_multiple_buffers() {
		_sut.AllocateAtLeast(99);
		_sut.AllocateAtLeast(100);
		_sut.AllocateAtLeast(101);
		Assert.Equal<string>(
			[
				"Allocated buffer 0 with 100 bytes", // respects minimum
				"Allocated buffer 1 with 100 bytes",
				"Allocated buffer 2 with 101 bytes", // can allocate larger
			],
			_recorder.GetNewMessages());
	}

	[Fact]
	public void can_reuse_buffers() {
		var p0 = _sut.AllocateAtLeast(100);
		_sut.Free(p0);

		_sut.AllocateAtLeast(101);
		_sut.AllocateAtLeast(100); // reuse p1
		_sut.AllocateAtLeast(101);

		Assert.Equal<string>(
			[
				"Allocated buffer 0 with 100 bytes",
				"Allocated buffer 1 with 101 bytes",
				"Allocated buffer 2 with 101 bytes",
			],
			_recorder.GetNewMessages());
	}

	[Fact]
	public void can_reuse_smaller_buffers() {
		var p0 = _sut.AllocateAtLeast(90);
		_sut.Free(p0);

		_sut.AllocateAtLeast(95);

		Assert.Equal<string>(["Allocated buffer 0 with 100 bytes"], _recorder.GetNewMessages());
	}

	class FakeInnerManager : IChunkCacheManager {
		readonly List<string> _messages = [];
		nint _nextBuffer;

		public string[] GetNewMessages() {
			var newMessages = _messages.ToArray();
			_messages.Clear();
			return newMessages;
		}

		public nint AllocateAtLeast(int numBytes) {
			var p = _nextBuffer++;
			_messages.Add($"Allocated buffer {p} with {numBytes} bytes");
			return p;
		}

		public void Free(nint pointer) {
			_messages.Add($"Freed buffer {pointer}");
		}
	}
}
