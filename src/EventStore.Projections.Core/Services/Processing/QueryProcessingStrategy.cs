using System;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Services.UserManagement;
using EventStore.Projections.Core.Messages;
using ILogger = Serilog.ILogger;

namespace EventStore.Projections.Core.Services.Processing {
	public class QueryProcessingStrategy : DefaultProjectionProcessingStrategy {
		public QueryProcessingStrategy(
			string name, ProjectionVersion projectionVersion, IProjectionStateHandler stateHandler,
			ProjectionConfig projectionConfig, IQuerySources sourceDefinition, ILogger logger,
			ReaderSubscriptionDispatcher subscriptionDispatcher, bool enableContentTypeValidation)
			: base(
				name, projectionVersion, stateHandler, projectionConfig, sourceDefinition, logger,
				subscriptionDispatcher, enableContentTypeValidation) {
		}

		public override bool GetStopOnEof() {
			return true;
		}

		public override bool GetUseCheckpoints() {
			return false;
		}

		public override bool GetProducesRunningResults() {
			return !_sourceDefinition.DefinesFold;
		}

		protected override IProjectionProcessingPhase[] CreateProjectionProcessingPhases(
			IPublisher publisher, IPublisher inputQueue, Guid projectionCorrelationId,
			ProjectionNamesBuilder namingBuilder,
			PartitionStateCache partitionStateCache, CoreProjection coreProjection, IODispatcher ioDispatcher,
			IProjectionProcessingPhase firstPhase) {
			var coreProjectionCheckpointWriter =
				new CoreProjectionCheckpointWriter(
					namingBuilder.MakeCheckpointStreamName(), ioDispatcher, _projectionVersion, _name);
			var checkpointManager2 = new DefaultCheckpointManager(
				publisher, projectionCorrelationId, _projectionVersion, SystemAccounts.System, ioDispatcher,
				_projectionConfig, _name, new PhasePositionTagger(1), namingBuilder, GetUseCheckpoints(), false,
				_sourceDefinition.DefinesFold, coreProjectionCheckpointWriter);

			IProjectionProcessingPhase writeResultsPhase;
			if (GetProducesRunningResults())
				writeResultsPhase = new WriteQueryEofProjectionProcessingPhase(
					publisher,
					1,
					namingBuilder.GetResultStreamName(),
					coreProjection,
					partitionStateCache,
					checkpointManager2,
					checkpointManager2,
					firstPhase.EmittedStreamsTracker);
			else
				writeResultsPhase = new WriteQueryResultProjectionProcessingPhase(
					publisher,
					1,
					namingBuilder.GetResultStreamName(),
					coreProjection,
					partitionStateCache,
					checkpointManager2,
					checkpointManager2,
					firstPhase.EmittedStreamsTracker);

			return new[] { firstPhase, writeResultsPhase };
		}

		protected override IResultEventEmitter CreateFirstPhaseResultEmitter(ProjectionNamesBuilder namingBuilder) {
			return new ResultEventEmitter(namingBuilder);
		}
	}
}
