// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Services.Storage.ReaderIndex;

public interface IExpiryStrategy {
	public DateTime? GetExpiry();
}

// Generates null expiry. Default expiry of now + ESConsts.ReadRequestTimeout will be in effect.
public class DefaultExpiryStrategy : IExpiryStrategy {
	public DateTime? GetExpiry() => null;
}
