// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Http;

namespace KurrentDB.Core.Services.Transport.Http.NodeHttpClientFactory;

/// <summary>
/// Registered by a host that has a way for the node to reach its own HTTP API other than the endpoint it
/// advertises in gossip, such as an embedded node, which listens only on a UNIX domain socket. A component
/// calling this node, and only this node, connects through the handler created here rather than to the
/// address it was given. The address then only supplies the scheme and the authority.
/// </summary>
public interface ILocalNodeHttpHandlerFactory {
	HttpMessageHandler CreateHandler();
}
