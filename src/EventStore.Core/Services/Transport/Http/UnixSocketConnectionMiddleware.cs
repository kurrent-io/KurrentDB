// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;

namespace EventStore.Core.Services.Transport.Http;
public class UnixSocketConnectionMiddleware(ConnectionDelegate next) {
	public const string UnixSocketConnectionKey = "UnixSocketConnection";

	public async Task OnConnectAsync(ConnectionContext context) {
		context.Items.Add(UnixSocketConnectionKey, true);
		await next(context);
	}
}
