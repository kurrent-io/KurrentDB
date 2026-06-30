// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;

namespace KurrentDB.KontrolPlane.StateMachine.LogEntries;

/// <summary>
/// Root interface for all log entries.
/// </summary>
internal interface ILogEntry : IMessage {

	/// <summary>
	/// Type identifier that is used to distinguish log entry types.
	/// </summary>
	public static abstract int TypeId { get; }
}

internal interface ILogEntry<TSelf> : ILogEntry, IProtobufSerializable<TSelf>
	where TSelf : class, ILogEntry<TSelf>;
