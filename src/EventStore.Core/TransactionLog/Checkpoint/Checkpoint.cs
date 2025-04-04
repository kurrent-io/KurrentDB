// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.TransactionLog.Checkpoint;

public static class Checkpoint {
	public const string Writer = "writer";
	public const string Chaser = "chaser";
	public const string Epoch = "epoch";
	public const string Proposal = "proposal";
	public const string Truncate = "truncate";
	public const string Replication = "replication";
	public const string Index = "index";
	public const string StreamExistenceFilter = "streamExistenceFilter";
}
