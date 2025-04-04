using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Tests.Services.projection_subscription;

namespace EventStore.Projections.Core.Tests.Services.event_reordering_projection_subscription {
	public abstract class TestFixtureWithEventReorderingProjectionSubscription : TestFixtureWithProjectionSubscription {
		protected int _timeBetweenEvents;
		protected int _processingLagMs;

		protected override void Given() {
			_timeBetweenEvents = 1100;
			_processingLagMs = 500;
			base.Given();
			_source = builder => {
				builder.FromStream("a");
				builder.FromStream("b");
				builder.AllEvents();
				builder.SetReorderEvents(true);
				builder.SetProcessingLag(1000); // ms
			};
		}

		protected override IReaderSubscription CreateProjectionSubscription() {
			return new EventReorderingReaderSubscription(_bus,
				_projectionCorrelationId,
				CheckpointTag.FromStreamPositions(0,
					new Dictionary<string, long> { { "a", ExpectedVersion.NoStream }, { "b", ExpectedVersion.NoStream } }),
				_readerStrategy,
				_timeProvider,
				_checkpointUnhandledBytesThreshold, _checkpointProcessedEventsThreshold, _checkpointAfterMs,
				_processingLagMs,
				false,
				null,
				false);
		}
	}
}
