// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;
using ClientMessageWriteEvents = KurrentDB.Core.Tests.TestAdapters.ClientMessage.WriteEvents;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string), OperationResult.CommitTimeout)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), OperationResult.CommitTimeout)]
[TestFixture(typeof(LogFormat.V2), typeof(string), OperationResult.PrepareTimeout)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), OperationResult.PrepareTimeout)]
[TestFixture(typeof(LogFormat.V2), typeof(string), OperationResult.ForwardTimeout)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), OperationResult.ForwardTimeout)]
public class when_writing_the_projections_initialized_event_fails<TLogFormat, TStreamId>
	: TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private readonly OperationResult _failureCondition;

	public when_writing_the_projections_initialized_event_fails(OperationResult failureCondition) {
		_failureCondition = failureCondition;
	}

	protected override void Given() {
		AllWritesQueueUp();
		NoStream(ProjectionNamesBuilder.ProjectionsRegistrationStream);
	}

	protected override bool GivenInitializeSystemProjections() {
		return false;
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
	}

	[Test, Category("v8")]
	public void retries_writing_with_the_same_event_id() {
		int retryCount = 0;
		var projectionsInitializedWrite = _consumer.HandledMessages
			.OfType<ClientMessageWriteEvents>()
			.Last(x => x.EventStreamId == ProjectionNamesBuilder.ProjectionsRegistrationStream &&
			           x.Events[0].EventType == ProjectionEventTypes.ProjectionsInitialized);
		var eventId = projectionsInitializedWrite.Events[0].EventId;
		while (retryCount < 5) {
			Assert.AreEqual(eventId, projectionsInitializedWrite.Events[0].EventId);
			projectionsInitializedWrite.Envelope.ReplyWith(new ClientMessage.WriteEventsCompleted(
				projectionsInitializedWrite.CorrelationId, _failureCondition,
				Enum.GetName(typeof(OperationResult), _failureCondition)));
			_queue.Process();
			projectionsInitializedWrite = _consumer.HandledMessages
				.OfType<ClientMessageWriteEvents>()
				.LastOrDefault(x => x.EventStreamId == ProjectionNamesBuilder.ProjectionsRegistrationStream &&
				                    x.Events[0].EventType == ProjectionEventTypes.ProjectionsInitialized);
			if (projectionsInitializedWrite != null) {
				retryCount++;
			}

			_consumer.HandledMessages.Clear();
		}
	}
}
