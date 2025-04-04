using EventStore.Common.Options;
using EventStore.Core.Bus;

namespace EventStore.Projections.Core {
	public class ProjectionsStandardComponents {
		public ProjectionsStandardComponents(
			int projectionWorkerThreadCount,
			ProjectionType runProjections,
			IBus leaderOutputBus,
			IQueuedHandler leaderInputQueue,
			IBus leaderMainBus,
			bool faultOutOfOrderProjections, int projectionCompilationTimeout, int projectionExecutionTimeout) {
			ProjectionWorkerThreadCount = projectionWorkerThreadCount;
			RunProjections = runProjections;
			LeaderOutputBus = leaderOutputBus;
			LeaderInputQueue = leaderInputQueue;
			LeaderMainBus = leaderMainBus;
			FaultOutOfOrderProjections = faultOutOfOrderProjections;
			ProjectionCompilationTimeout = projectionCompilationTimeout;
			ProjectionExecutionTimeout = projectionExecutionTimeout;
		}

		public int ProjectionWorkerThreadCount { get; }

		public ProjectionType RunProjections { get; }

		public IBus LeaderOutputBus { get; }

		public IQueuedHandler LeaderInputQueue { get; }

		public IBus LeaderMainBus { get; }

		public bool FaultOutOfOrderProjections { get; }

		public int ProjectionCompilationTimeout { get; }

		public int ProjectionExecutionTimeout { get; }
	}
}
