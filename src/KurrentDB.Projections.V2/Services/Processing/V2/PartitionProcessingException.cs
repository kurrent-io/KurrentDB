// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

/// <summary>
/// An event-processing failure inside a partition processor. The message is the
/// projection's fault reason as surfaced in stateReason: projection name, handler
/// type, event position, and the handler's error. Same format as
/// EventProcessingProjectionProcessingPhase.SafeProcessEventByHandler, so operators
/// and tooling see one fault-reason shape regardless of engine version; keep in sync.
/// </summary>
public sealed class PartitionProcessingException : Exception {
	public PartitionProcessingException(string projectionName, Type handlerType, string eventPosition, Exception inner)
		: base(
			$"""
			The {projectionName} projection failed to process an event.
			Handler: {handlerType.Namespace}.{handlerType.Name}
			Event Position: {eventPosition}

			Message:

			{inner.Message}
			""",
			inner) {
	}
}
