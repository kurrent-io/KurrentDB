// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Infrastructure;

namespace EventStore.Core.Tests.Services.ElectionsService.Randomized;

internal class SendOverGrpcProcessor : IHandle<GrpcMessage.SendOverGrpc> {
	private readonly Random _rnd;
	private readonly Dictionary<EndPoint, IPublisher> _httpBuses = new Dictionary<EndPoint, IPublisher>();
	private readonly RandomTestRunner _runner;
	private readonly double _lossProb;
	private readonly double _dupProb;
	private readonly int _maxDelay;

	public SendOverGrpcProcessor(Random rnd, RandomTestRunner runner, double lossProb, double dupProb,
		int maxDelay) {
		if (rnd == null) throw new ArgumentNullException("rnd");
		if (runner == null) throw new ArgumentNullException("runner");
		if (lossProb < 0.0 || lossProb > 1.0) throw new ArgumentOutOfRangeException("lossProb");
		if (dupProb < 0.0 || dupProb > 1.0) throw new ArgumentOutOfRangeException("dupProb");
		if (maxDelay <= 0) throw new ArgumentOutOfRangeException("maxDelay");

		_rnd = rnd;
		_runner = runner;
		_lossProb = lossProb;
		_dupProb = dupProb;
		_maxDelay = maxDelay;
	}

	public void RegisterEndPoint(EndPoint endPoint, IPublisher bus) {
		_httpBuses.Add(endPoint, bus);
	}

	public void Handle(GrpcMessage.SendOverGrpc message) {
		if (_rnd.NextDouble() < _lossProb)
			return;

		if (ShouldSkipMessage(message))
			return;

		IPublisher publisher;
		if (!_httpBuses.TryGetValue(message.DestinationEndpoint, out publisher))
			throw new InvalidOperationException(string.Format("No HTTP bus subscribed for EndPoint: {0}.",
				message.DestinationEndpoint));

		_runner.Enqueue(message.DestinationEndpoint, message.Message, publisher, 1 + _rnd.Next(_maxDelay));

		if (_rnd.NextDouble() < _dupProb)
			_runner.Enqueue(message.DestinationEndpoint, message.Message, publisher, 1 + _rnd.Next(_maxDelay));
	}

	protected virtual bool ShouldSkipMessage(GrpcMessage.SendOverGrpc message) {
		return false;
	}
}
