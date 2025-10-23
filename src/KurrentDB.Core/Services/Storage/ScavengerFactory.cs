// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messages;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.Scavenging.Interfaces;
using Serilog;

namespace KurrentDB.Core.Services.Storage;

// The resulting scavenger is used for one continuous run. If it is cancelled or
// completed then starting scavenge again will instantiate another scavenger
// with a different id.
public class ScavengerFactory(Func<ClientMessage.ScavengeDatabase, ITFChunkScavengerLog, ILogger, IScavenger> create) {
	public IScavenger Create(ClientMessage.ScavengeDatabase message, ITFChunkScavengerLog scavengerLogger, ILogger logger) => create(message, scavengerLogger, logger);
}
