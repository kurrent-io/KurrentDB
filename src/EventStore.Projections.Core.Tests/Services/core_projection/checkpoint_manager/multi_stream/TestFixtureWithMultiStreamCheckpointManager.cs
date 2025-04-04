using EventStore.Projections.Core.Services.Processing;

namespace EventStore.Projections.Core.Tests.Services.core_projection.checkpoint_manager.multi_stream {
	public abstract class TestFixtureWithMultiStreamCheckpointManager<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionCheckpointManager<TLogFormat, TStreamId> {
		protected new string[] _streams;

		protected override void Given() {
			base.Given();
			_projectionVersion = new ProjectionVersion(1, 0, 0);
			_streams = new[] { "a", "b", "c" };
		}

		protected override DefaultCheckpointManager GivenCheckpointManager() {
			return new MultiStreamMultiOutputCheckpointManager(
				_bus, _projectionCorrelationId, _projectionVersion, null, _ioDispatcher, _config, _projectionName,
				new MultiStreamPositionTagger(0, _streams), _namingBuilder, _checkpointsEnabled, true, true,
				_checkpointWriter);
		}
	}
}
