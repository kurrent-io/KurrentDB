// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Messaging;
using EventStore.Transport.Http.EntityManagement;

namespace EventStore.Core.Services.Transport.Http.Messages;

[DerivedMessage(CoreMessage.Http)]
partial class AuthenticatedHttpRequestMessage : Message {
	public readonly HttpEntityManager Manager;
	public readonly UriToActionMatch Match;

	public AuthenticatedHttpRequestMessage(HttpEntityManager manager, UriToActionMatch match) {
		Manager = manager;
		Match = match;
	}
}
