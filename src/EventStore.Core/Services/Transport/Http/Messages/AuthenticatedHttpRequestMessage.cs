// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Messaging;
using EventStore.Transport.Http.EntityManagement;

namespace EventStore.Core.Services.Transport.Http.Messages;

[DerivedMessage(CoreMessage.Http)]
partial class AuthenticatedHttpRequestMessage(HttpEntityManager manager, UriToActionMatch match) : Message {
	public readonly HttpEntityManager Manager = manager;
	public readonly UriToActionMatch Match = match;
}
