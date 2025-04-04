using System;

namespace EventStore.Projections.Core.Services.Processing {
	public class ResultEventEmitter : IResultEventEmitter {
		private readonly ProjectionNamesBuilder _namesBuilder;

		private readonly EmittedStream.WriterConfiguration.StreamMetadata _resultStreamMetadata =
			new EmittedStream.WriterConfiguration.StreamMetadata( /* TBD */);

		public ResultEventEmitter(ProjectionNamesBuilder namesBuilder) {
			if (namesBuilder == null)
				throw new ArgumentNullException("namesBuilder");
			_namesBuilder = namesBuilder;
		}

		public EmittedEventEnvelope[] ResultUpdated(string partition, string result, CheckpointTag at) {
			return CreateResultUpdatedEvents(partition, result, at);
		}

		private EmittedEventEnvelope[] CreateResultUpdatedEvents(string partition, string projectionResult,
			CheckpointTag at) {
			var streamId = _namesBuilder.MakePartitionResultStreamName(partition);
			var allResultsStreamId = _namesBuilder.GetResultStreamName();
			if (string.IsNullOrEmpty(partition)) {
				var result =
					new EmittedEventEnvelope(
						projectionResult == null
							? new EmittedDataEvent(
								streamId, Guid.NewGuid(), "ResultRemoved", true, null, null, at, null)
							: new EmittedDataEvent(
								streamId, Guid.NewGuid(), "Result", true, projectionResult, null, at, null),
						_resultStreamMetadata);

				return new[] { result };
			} else {
				var linkTo = new EmittedLinkTo(allResultsStreamId, Guid.NewGuid(), streamId, at, null);
				var linkToEnvelope = new EmittedEventEnvelope(linkTo, _resultStreamMetadata);
				var result =
					new EmittedEventEnvelope(
						projectionResult == null
							? new EmittedDataEvent(
								streamId, Guid.NewGuid(), "ResultRemoved", true, null, null, at, null,
								linkTo.SetTargetEventNumber)
							: new EmittedDataEvent(
								streamId, Guid.NewGuid(), "Result", true, projectionResult, null, at, null,
								linkTo.SetTargetEventNumber), _resultStreamMetadata);
				return new[] { result, linkToEnvelope };
			}
		}
	}
}
