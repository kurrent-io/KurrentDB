// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Net;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Infrastructure;

namespace EventStore.Core.Tests.Services.ElectionsService.Randomized;

internal class ElectionsSafetyCondition : IRandTestFinishCondition {
	public bool Done { get; private set; }

	public bool Success {
		get {
			if (_electionsResults.Count == 0)
				return false;
			var leader = _electionsResults.First().Value;
			return _electionsResults.Values.All(x => x.Equals(leader)); // same leader for all
		}
	}

	private readonly int _instancesCount;

	private readonly Dictionary<EndPoint, EndPoint>
		_electionsResults = new Dictionary<EndPoint, EndPoint>();

	public ElectionsSafetyCondition(int instancesCount) {
		_instancesCount = instancesCount;
	}

	public void Process(int iteration, RandTestQueueItem item) {
		var electionsMsg = item.Message as ElectionMessage.ElectionsDone;
		if (electionsMsg != null) {
			_electionsResults[item.EndPoint] = electionsMsg.Leader.HttpEndPoint;
			Done = _electionsResults.Count == _instancesCount;
		}
	}

	public void Log() {
	}
}
