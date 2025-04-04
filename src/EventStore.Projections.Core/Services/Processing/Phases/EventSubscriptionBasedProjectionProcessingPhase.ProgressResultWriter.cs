// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing.Strategies;

namespace EventStore.Projections.Core.Services.Processing.Phases;

public abstract partial class EventSubscriptionBasedProjectionProcessingPhase {
	internal class ProgressResultWriter : IProgressResultWriter {
		private readonly EventSubscriptionBasedProjectionProcessingPhase _phase;
		private readonly IResultWriter _resultWriter;

		public ProgressResultWriter(EventSubscriptionBasedProjectionProcessingPhase phase,
			IResultWriter resultWriter) {
			_phase = phase;
			_resultWriter = resultWriter;
		}

		public void WriteProgress(float progress) {
			_resultWriter.WriteProgress(_phase._currentSubscriptionId, progress);
		}
	}
}
