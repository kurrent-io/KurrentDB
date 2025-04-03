// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;

namespace EventStore.Core.Services.Transport.Http;

public static class HttpContextExtensions {
	public static bool IsUnixSocketConnection(this HttpContext ctx) {
		var connectionItemsFeature = ctx.Features.Get<IConnectionItemsFeature>();
		return connectionItemsFeature is not null && connectionItemsFeature.Items.ContainsKey(UnixSocketConnectionMiddleware.UnixSocketConnectionKey);
	}
}
