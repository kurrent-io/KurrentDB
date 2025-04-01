// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.TimerService;
using Serilog;

namespace EventStore.Core.Services;

// This intercepts the ShutdownRequest and shuts down Peripheral Services before shutting down the Core.
// todo: later we can move the logic that tracks the core service shutdown into here, and also subsystems shutdown.
public class ShutdownService :
	IHandle<ClientMessage.RequestShutdown>,
	IHandle<SystemMessage.RegisterForGracefulTermination>,
	IHandle<SystemMessage.ComponentTerminated>,
	IHandle<SystemMessage.PeripheralShutdownTimeout> {

	private static readonly ILogger Log = Serilog.Log.ForContext<ShutdownService>();
	private static readonly TimeSpan PeripheryShutdownTimeout = TimeSpan.FromSeconds(30);

	private readonly IPublisher _mainQueue;
	private readonly VNodeInfo _nodeInfo;
	private readonly Dictionary<string, Action> _shutdownActions = [];
	private readonly TimeSpan _shutdownTimeout;

	private bool _exitProcess;
	private bool _shutdownHttp;

	enum State {
		Running,
		ShuttingDownPeriphery,
		ShuttingDownCore,
	}
	private State _state;

	public ShutdownService(IPublisher mainQueue, VNodeInfo nodeInfo, TimeSpan? shutdownTimeout = null) {
		_mainQueue = mainQueue;
		_nodeInfo = nodeInfo;
		_shutdownTimeout = shutdownTimeout ?? PeripheryShutdownTimeout;
	}

	public void Handle(SystemMessage.RegisterForGracefulTermination message) {
		if (_state is not State.Running) {
			Log.Warning("Component {ComponentName} tried to register for graceful shutdown while the server is shutting down",
				message.ComponentName);
			return;
		}

		if (!_shutdownActions.TryAdd(message.ComponentName, message.Action))
			throw new InvalidOperationException($"Component {message.ComponentName} already registered");

		Log.Information("========== [{HttpEndPoint}] Component '{Component}' is registered for graceful termination", _nodeInfo.HttpEndPoint,
			message.ComponentName);
	}

	public void Handle(ClientMessage.RequestShutdown message) {
		if (_state is not State.Running) {
			Log.Debug("Ignored request shutdown message because the server is already shutting down");
			return;
		}

		_exitProcess = message.ExitProcess;
		_shutdownHttp = message.ShutdownHttp;

		if (_shutdownActions.Count == 0) {
			ShutDownCore();
			return;
		}

		_state = State.ShuttingDownPeriphery;
		Log.Information("========== [{httpEndPoint}] IS SHUTTING DOWN PERIPHERAL COMPONENTS...", _nodeInfo.HttpEndPoint);

		foreach (var entry in _shutdownActions) {
			try {
				entry.Value();
			} catch (Exception e) {
				Log.Warning(e, "Component {ComponentName} faulted when initiating shutdown", entry.Key);
			}
		}

		_mainQueue.Publish(TimerMessage.Schedule.Create(
			_shutdownTimeout,
			_mainQueue,
			new SystemMessage.PeripheralShutdownTimeout()));
	}

	public void Handle(SystemMessage.ComponentTerminated message) {
		if (!_shutdownActions.Remove(message.ComponentName))
			throw new InvalidOperationException($"Component {message.ComponentName} already terminated");

		Log.Information("========== [{HttpEndPoint}] Component '{ComponentName}' has shut down.", _nodeInfo.HttpEndPoint,
			message.ComponentName);

		if (_state is State.ShuttingDownPeriphery && _shutdownActions.Count == 0) {
			Log.Information("========== [{HttpEndPoint}] All Components Shutdown.", _nodeInfo.HttpEndPoint);
			ShutDownCore();
		}
	}

	public void Handle(SystemMessage.PeripheralShutdownTimeout message) {
		if (_state is not State.ShuttingDownPeriphery) {
			Log.Debug("Ignored shutdown timeout message when in state {State}", _state);
			return;
		}

		Log.Information("========== [{httpEndPoint}] TIMED OUT SHUTTING DOWN PERIPHERAL COMPONENTS {Components}...",
			_nodeInfo.HttpEndPoint,
			string.Join(", ", _shutdownActions.Keys));
		ShutDownCore();
	}

	private void ShutDownCore() {
		_state = State.ShuttingDownCore;
		_mainQueue.Publish(new SystemMessage.BecomeShuttingDown(
			correlationId: Guid.NewGuid(),
			exitProcess: _exitProcess,
			shutdownHttp: _shutdownHttp));
	}
}
