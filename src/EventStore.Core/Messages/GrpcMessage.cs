// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Core.Messaging;

namespace EventStore.Core.Messages;

public static partial class GrpcMessage {
	[DerivedMessage(CoreMessage.Grpc)]
	public partial class SendOverGrpc : Message {

		public readonly EndPoint DestinationEndpoint;
		public readonly Message Message;
		public readonly DateTime LiveUntil;

		public SendOverGrpc(EndPoint destinationEndpoint, Message message, DateTime liveUntil) {
			DestinationEndpoint = destinationEndpoint;
			Message = message;
			LiveUntil = liveUntil;
		}
	}
}
