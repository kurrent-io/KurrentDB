// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests;

public class LotsOfExpiriesStrategy : IExpiryStrategy {
	private int _counter;

	public DateTime? GetExpiry() {
		_counter++;
		if (_counter % 10 == 0) {
			// ok
			return null;
		} else {
			// expired already
			return DateTime.UtcNow - TimeSpan.FromSeconds(1);
		}
	}
}
