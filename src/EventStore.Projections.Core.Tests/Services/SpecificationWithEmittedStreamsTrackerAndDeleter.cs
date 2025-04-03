// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Tests.ClientAPI;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Emitting;

namespace EventStore.Projections.Core.Tests.Services;

public abstract class SpecificationWithEmittedStreamsTrackerAndDeleter<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	protected IEmittedStreamsTracker _emittedStreamsTracker;
	protected IEmittedStreamsDeleter _emittedStreamsDeleter;
	protected ProjectionNamesBuilder _projectionNamesBuilder;
	protected ClientMessage.ReadStreamEventsForwardCompleted _readCompleted;
	protected IODispatcher _ioDispatcher;
	protected bool _trackEmittedStreams = true;
	protected string _projectionName = "test_projection";

	protected override Task Given() {
		_ioDispatcher = new IODispatcher(_node.Node.MainQueue, _node.Node.MainQueue, true);
		_node.Node.MainBus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_ioDispatcher.BackwardReader);
		_node.Node.MainBus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher.BackwardReader);
		_node.Node.MainBus.Subscribe(_ioDispatcher.ForwardReader);
		_node.Node.MainBus.Subscribe(_ioDispatcher.Writer);
		_node.Node.MainBus.Subscribe(_ioDispatcher.StreamDeleter);
		_node.Node.MainBus.Subscribe(_ioDispatcher.Awaker);
		_node.Node.MainBus.Subscribe<IODispatcherDelayedMessage>(_ioDispatcher);
		_node.Node.MainBus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher);
		_projectionNamesBuilder = ProjectionNamesBuilder.CreateForTest(_projectionName);
		_emittedStreamsTracker = new EmittedStreamsTracker(_ioDispatcher,
			new ProjectionConfig(null, 1000, 1000 * 1000, 100, 500, true, true, false, false,
				_trackEmittedStreams, 10000, 1, null), _projectionNamesBuilder);
		_emittedStreamsDeleter = new EmittedStreamsDeleter(_ioDispatcher,
			_projectionNamesBuilder.GetEmittedStreamsName(),
			_projectionNamesBuilder.GetEmittedStreamsCheckpointName());
		return Task.CompletedTask;
	}
}
