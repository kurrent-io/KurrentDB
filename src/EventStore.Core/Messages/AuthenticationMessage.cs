// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Messaging;

namespace EventStore.Core.Messages;

public static partial class AuthenticationMessage {
	[DerivedMessage(CoreMessage.Authentication)]
	public partial class AuthenticationProviderInitialized : Message {
	}

	[DerivedMessage(CoreMessage.Authentication)]
	public partial class AuthenticationProviderInitializationFailed : Message {
	}
}
