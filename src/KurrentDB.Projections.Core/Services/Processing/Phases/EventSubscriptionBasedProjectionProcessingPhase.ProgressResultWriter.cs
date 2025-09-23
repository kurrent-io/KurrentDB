// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing.Strategies;

namespace KurrentDB.Projections.Core.Services.Processing.Phases;

public abstract partial class EventSubscriptionBasedProjectionProcessingPhase {
	internal class ProgressResultWriter(IResultWriter resultWriter) : IProgressResultWriter {
		public void WriteProgress(float progress) {
			resultWriter.WriteProgress();
		}
	}
}
