// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Messaging;
using KurrentDB.Transport.Http.EntityManagement;

namespace KurrentDB.Core.Services.Transport.Http.Messages;

[DerivedMessage(CoreMessage.Http)]
partial class AuthenticatedHttpRequestMessage(HttpEntityManager manager, UriToActionMatch match) : Message {
	public readonly HttpEntityManager Manager = manager;
	public readonly UriToActionMatch Match = match;
}
