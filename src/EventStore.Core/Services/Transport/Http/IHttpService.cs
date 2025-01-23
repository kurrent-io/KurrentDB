// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Transport.Http.EntityManagement;

namespace EventStore.Core.Services.Transport.Http;

public interface IHttpController {
	void Subscribe(IUriRouter router);
}

public interface IHttpForwarder {
	bool ForwardRequest(HttpEntityManager manager);
}
