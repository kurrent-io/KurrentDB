// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;

namespace KurrentDB.Core.Services;

// Called by worker bus concurrently. Thread Safe.
public class TcpSendService : IHandle<TcpMessage.TcpSend> {
	public void Handle(TcpMessage.TcpSend message) {
		// todo: histogram metric?
		message.ConnectionManager.SendMessage(message.Message);
	}
}
