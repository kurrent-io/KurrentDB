using System;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.emitted_stream {
	[TestFixture]
	public class a_checkpoint_requested_on_a_non_started_stream : TestFixtureWithReadWriteDispatchers {
		private EmittedStream _stream;

		private TestCheckpointManagerMessageHandler _readyHandler;
		private Exception _caughtException;

		[SetUp]
		public void setup() {
			_readyHandler = new TestCheckpointManagerMessageHandler();
			;
			_stream = new EmittedStream(
				"test",
				new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
					new EmittedStream.WriterConfiguration.StreamMetadata(), null, 50), new ProjectionVersion(1, 0, 0),
				new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _bus, _ioDispatcher,
				_readyHandler);
			try {
				_stream.Checkpoint();
			} catch (Exception ex) {
				_caughtException = ex;
			}
		}

		[Test]
		public void throws_invalid_operation_exception() {
			Assert.Throws<InvalidOperationException>(() => {
				if (_caughtException != null)
					throw _caughtException;
			});
		}
	}
}
