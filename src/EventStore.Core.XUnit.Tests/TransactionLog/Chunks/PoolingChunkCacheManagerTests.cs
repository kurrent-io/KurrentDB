using System.Collections.Generic;
using EventStore.Core.TransactionLog.Chunks;
using Xunit;

namespace EventStore.Core.XUnit.Tests.TransactionLog.Chunks;

public class PoolingChunkCacheManagerTests {
	readonly FakeInnerManager _recorder = new();

	private PoolingChunkCacheManager CreateSut(int initialBuffers) =>
		new(_recorder, minBufferSize: 100, cleanBuffers: false, initialBuffers: initialBuffers);

	[Fact]
	public void can_allocate_multiple_buffers() {
		var sut = CreateSut(initialBuffers: 0);
		sut.AllocateAtLeast(99);
		sut.AllocateAtLeast(100);
		sut.AllocateAtLeast(101);
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
		var sut = CreateSut(initialBuffers: 0);
		var p0 = sut.AllocateAtLeast(100);
		sut.Free(p0);

		sut.AllocateAtLeast(101);
		sut.AllocateAtLeast(100); // reuse p1
		sut.AllocateAtLeast(101);

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
		var sut = CreateSut(initialBuffers: 0);
		var p0 = sut.AllocateAtLeast(90);
		sut.Free(p0);

		sut.AllocateAtLeast(95);

		Assert.Equal<string>(["Allocated buffer 0 with 100 bytes"], _recorder.GetNewMessages());
	}

	[Fact]
	public void can_create_initial_buffers() {
		var sut = CreateSut(initialBuffers: 3);

		Assert.Equal<string>(
			[
				"Allocated buffer 0 with 100 bytes",
				"Allocated buffer 1 with 100 bytes",
				"Allocated buffer 2 with 100 bytes",
			],
			_recorder.GetNewMessages());

		sut.AllocateAtLeast(100);
		sut.AllocateAtLeast(100);
		sut.AllocateAtLeast(100);

		Assert.Equal<string>([], _recorder.GetNewMessages());
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
