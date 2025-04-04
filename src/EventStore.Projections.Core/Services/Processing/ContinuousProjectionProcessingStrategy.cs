using System;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Projections.Core.Messages;
using ILogger = Serilog.ILogger;

namespace EventStore.Projections.Core.Services.Processing {
	public class ContinuousProjectionProcessingStrategy : DefaultProjectionProcessingStrategy {
		public ContinuousProjectionProcessingStrategy(
			string name, ProjectionVersion projectionVersion, IProjectionStateHandler stateHandler,
			ProjectionConfig projectionConfig, IQuerySources sourceDefinition, ILogger logger,
			ReaderSubscriptionDispatcher subscriptionDispatcher, bool enableContentTypeValidation)
			: base(
				name, projectionVersion, stateHandler, projectionConfig, sourceDefinition, logger,
				subscriptionDispatcher, enableContentTypeValidation) {
		}

		public override bool GetStopOnEof() {
			return false;
		}

		public override bool GetUseCheckpoints() {
			return _projectionConfig.CheckpointsEnabled;
		}

		public override bool GetProducesRunningResults() {
			return _sourceDefinition.ProducesResults;
		}

		protected override IProjectionProcessingPhase[] CreateProjectionProcessingPhases(
			IPublisher publisher,
			IPublisher inputQueue,
			Guid projectionCorrelationId,
			ProjectionNamesBuilder namingBuilder,
			PartitionStateCache partitionStateCache,
			CoreProjection coreProjection,
			IODispatcher ioDispatcher,
			IProjectionProcessingPhase firstPhase) {
			return new IProjectionProcessingPhase[] { firstPhase };
		}

		protected override IResultEventEmitter CreateFirstPhaseResultEmitter(ProjectionNamesBuilder namingBuilder) {
			return _sourceDefinition.ProducesResults
				? new ResultEventEmitter(namingBuilder)
				: (IResultEventEmitter)new NoopResultEventEmitter();
		}
	}
}
