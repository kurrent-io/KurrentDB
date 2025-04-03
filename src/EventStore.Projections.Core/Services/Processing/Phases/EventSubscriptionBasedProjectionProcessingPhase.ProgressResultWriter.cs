// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing.Strategies;

namespace EventStore.Projections.Core.Services.Processing.Phases;

public abstract partial class EventSubscriptionBasedProjectionProcessingPhase
{
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
