// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Transport.Http;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace KurrentDB.Core.Services.Transport.Http;

public class AuthorizationMiddleware(IAuthorizationProvider authorization) : IMiddleware {
	private static readonly ILogger Log = Serilog.Log.ForContext<AuthorizationMiddleware>();

	public async Task InvokeAsync(HttpContext context, RequestDelegate next) {
		if (InternalHttpHelper.TryGetInternalContext(context, out var manager, out var match, out _)) {
			if (await authorization.CheckAccessAsync(manager.User, match.ControllerAction.Operation(match.TemplateMatch), context.RequestAborted)) {
				await next(context);
				return;
			}

			context.Response.StatusCode = HttpStatusCode.Unauthorized;
			return;
		}

		// No internal context - this is not a legacy API request, continue to routing/Blazor
		await next(context);
	}
}
