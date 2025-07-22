// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Messaging;

namespace EventStore.Projections.Core.Services.Processing;

public class RequestResponseQueueForwarder : IHandle<ClientMessage.ReadEvent>,
	IHandle<ClientMessage.ReadStreamEventsBackward>,
	IHandle<ClientMessage.ReadStreamEventsForward>,
	IHandle<ClientMessage.ReadAllEventsForward>,
	IHandle<ClientMessage.WriteEvents>,
	IHandle<ClientMessage.DeleteStream>,
	IHandle<SystemMessage.SubSystemInitialized>,
	IHandle<ProjectionCoreServiceMessage.SubComponentStarted>,
	IHandle<ProjectionCoreServiceMessage.SubComponentStopped> {
	private readonly IPublisher _externalRequestQueue;
	private readonly IPublisher _inputQueue;

	public RequestResponseQueueForwarder(IPublisher inputQueue, IPublisher externalRequestQueue) {
		_inputQueue = inputQueue;
		_externalRequestQueue = externalRequestQueue;
	}

	public void Handle(ClientMessage.ReadEvent msg) {
		_externalRequestQueue.Publish(
			new ClientMessage.ReadEvent(
				msg.InternalCorrId, msg.CorrelationId, new PublishToWrapEnvelop(_inputQueue, msg.Envelope, nameof(ClientMessage.ReadEvent)),
				msg.EventStreamId, msg.EventNumber, msg.ResolveLinkTos, msg.RequireLeader, msg.User));
	}

	public void Handle(ClientMessage.WriteEvents msg) {
		_externalRequestQueue.Publish(
			new ClientMessage.WriteEvents(
				msg.InternalCorrId, msg.CorrelationId, new PublishToWrapEnvelop(_inputQueue, msg.Envelope, nameof(ClientMessage.WriteEvents)), true,
				msg.EventStreamId, msg.ExpectedVersion, msg.Events, msg.User));
	}

	public void Handle(ClientMessage.DeleteStream msg) {
		_externalRequestQueue.Publish(
			new ClientMessage.DeleteStream(
				msg.InternalCorrId, msg.CorrelationId, new PublishToWrapEnvelop(_inputQueue, msg.Envelope, nameof(ClientMessage.DeleteStream)), true,
				msg.EventStreamId, msg.ExpectedVersion, msg.HardDelete, msg.User));
	}

	// Historically the forwarding we do here has discarded the Expiration of the msg when forwarding it, resetting it
	// to the default, which is 10 seconds from now. We should probably propagate the Expiration of all the source messages.
	// However, in this fix we make the minimum impact change necessary which is to only pass the expiration that we
	// need (DateTime.MaxValue) and only for the messages that we need.
	public void Handle(ClientMessage.ReadStreamEventsBackward msg) {
		_externalRequestQueue.Publish(
			new ClientMessage.ReadStreamEventsBackward(
				msg.InternalCorrId, msg.CorrelationId, new PublishToWrapEnvelop(_inputQueue, msg.Envelope, nameof(ClientMessage.ReadStreamEventsBackward)),
				msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, msg.ResolveLinkTos, msg.RequireLeader,
				msg.ValidationStreamVersion, msg.User,
				expires: msg.Expires == DateTime.MaxValue ? msg.Expires : null));
	}

	public void Handle(ClientMessage.ReadStreamEventsForward msg) {
		_externalRequestQueue.Publish(
			new ClientMessage.ReadStreamEventsForward(
				msg.InternalCorrId, msg.CorrelationId, new PublishToWrapEnvelop(_inputQueue, msg.Envelope, nameof(ClientMessage.ReadStreamEventsForward)),
				msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, msg.ResolveLinkTos, msg.RequireLeader,
				msg.ValidationStreamVersion, msg.User, replyOnExpired: false,
				expires: msg.Expires == DateTime.MaxValue ? msg.Expires : null));
	}

	public void Handle(ClientMessage.ReadAllEventsForward msg) {
		_externalRequestQueue.Publish(
			new ClientMessage.ReadAllEventsForward(
				msg.InternalCorrId, msg.CorrelationId, new PublishToWrapEnvelop(_inputQueue, msg.Envelope, nameof(ClientMessage.ReadAllEventsForward)),
				msg.CommitPosition, msg.PreparePosition, msg.MaxCount, msg.ResolveLinkTos, msg.RequireLeader,
				msg.ValidationTfLastCommitPosition, msg.User, replyOnExpired: false));
	}

	public void Handle(SystemMessage.SubSystemInitialized msg) {
		_externalRequestQueue.Publish(
			new SystemMessage.SubSystemInitialized(msg.SubSystemName));
	}

	void IHandle<ProjectionCoreServiceMessage.SubComponentStarted>.Handle(
		ProjectionCoreServiceMessage.SubComponentStarted message) {
		_externalRequestQueue.Publish(
			new ProjectionCoreServiceMessage.SubComponentStarted(message.SubComponent, message.InstanceCorrelationId)
		);
	}

	void IHandle<ProjectionCoreServiceMessage.SubComponentStopped>.Handle(
		ProjectionCoreServiceMessage.SubComponentStopped message) {
		_externalRequestQueue.Publish(
			new ProjectionCoreServiceMessage.SubComponentStopped(message.SubComponent, message.QueueId)
		);
	}
}
