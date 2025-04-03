// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Core.Bus;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Infrastructure;

namespace EventStore.Core.Tests.Services.ElectionsService.Randomized;

internal class TimerMessageProcessor : IHandle<TimerMessage.Schedule> {
	private readonly Random _rnd;
	private readonly RandomTestRunner _runner;
	private readonly IPEndPoint _endPoint;
	private readonly IPublisher _bus;
	private readonly int _delayMin;
	private readonly int _delayMax;

	public TimerMessageProcessor(Random rnd,
		RandomTestRunner runner,
		IPEndPoint endPoint,
		IPublisher bus,
		int delayMin,
		int delayMax) {
		if (rnd == null) throw new ArgumentNullException("rnd");
		if (runner == null) throw new ArgumentNullException("runner");
		if (endPoint == null) throw new ArgumentNullException("endPoint");
		if (bus == null) throw new ArgumentNullException("bus");
		if (delayMin <= 0) throw new ArgumentOutOfRangeException("delayMin");
		if (delayMin >= delayMax) throw new ArgumentException("DelayMin should be strictly less than DelayMax.");

		_rnd = rnd;
		_runner = runner;
		_endPoint = endPoint;
		_bus = bus;
		_delayMin = delayMin;
		_delayMax = delayMax;
	}

	public void Handle(TimerMessage.Schedule message) {
		_runner.Enqueue(_endPoint, message.ReplyMessage, _bus, _rnd.Next(_delayMin, _delayMax));
	}
}
