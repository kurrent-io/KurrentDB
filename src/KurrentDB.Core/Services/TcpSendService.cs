// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Messages;
using KurrentDB.Core.Bus;

namespace KurrentDB.Core.Services;

public class TcpSendService : IHandle<TcpMessage.TcpSend> {
	public void Handle(TcpMessage.TcpSend message) {
		// todo: histogram metric?
		message.ConnectionManager.SendMessage(message.Message);
	}
}
