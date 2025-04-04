// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Security.Claims;
using EventStore.Core.Messaging;

namespace EventStore.Core.Services.Transport.Tcp;

public interface ITcpDispatcher {
	TcpPackage? WrapMessage(Message message, byte version);

	Message UnwrapPackage(TcpPackage package, IEnvelope envelope, ClaimsPrincipal user,
		IReadOnlyDictionary<string, string> tokens, TcpConnectionManager connection, byte version);
}
