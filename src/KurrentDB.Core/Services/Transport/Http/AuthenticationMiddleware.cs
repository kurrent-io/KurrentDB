// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Plugins.Authentication;
using Grpc.Core;
using KurrentDB.Transport.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Newtonsoft.Json;

namespace KurrentDB.Core.Services.Transport.Http;

public class AuthenticationMiddleware : IMiddleware {
	private readonly IAuthenticationProvider _authenticationProvider;
	private readonly IReadOnlyList<IHttpAuthenticationProvider> _httpAuthenticationProviders;

	public AuthenticationMiddleware(
		IReadOnlyList<IHttpAuthenticationProvider> httpAuthenticationProviders,
		IAuthenticationProvider authenticationProvider) {
		_httpAuthenticationProviders = httpAuthenticationProviders;
		_authenticationProvider = authenticationProvider;
	}

	public async Task InvokeAsync(HttpContext context, RequestDelegate next) {
		try {
			if (context.IsGrpc()) {
				await HandleAsGrpcAsync(context, next);
			} else {
				await HandleAsHttpAsync(context, next);
			}
		} catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException) {
			// ignore request aborted
		}
	}

	private async Task HandleAsHttpAsync(HttpContext context, RequestDelegate next) {
		if (!TrySelectProvider(context, out var authenticationRequest)) {
			await AddHttp1ChallengeHeaders(context);
			return;
		}

		var (status, principal) = await authenticationRequest.AuthenticateAsync();

		switch (status) {
			case HttpAuthenticationRequestStatus.Authenticated:
				context.User = principal;
				await next(context);
				if (context.Response.StatusCode == 302 && principal.Identity?.IsAuthenticated == false) {
					// Unless the call is made from a browser, return 401 instead of redirecting to the login page
					var userAgent = context.Request.Headers.UserAgent.FirstOrDefault();
					if (userAgent == null || userAgent.StartsWith("Mozilla") == false) {
						// Avoid setting the status code if the response has already started
						if (!context.Response.HasStarted) {
							context.Response.StatusCode = 401;
						}
					}
				}

				break;
			case HttpAuthenticationRequestStatus.Error:
				context.Response.StatusCode = HttpStatusCode.InternalServerError;
				break;
			case HttpAuthenticationRequestStatus.NotReady:
				context.Response.StatusCode = HttpStatusCode.ServiceUnavailable;
				context.Response.Headers.Append("Retry-After", "5");
				break;
			case HttpAuthenticationRequestStatus.Unauthenticated:
			default:
				await AddHttp1ChallengeHeaders(context);
				break;
		}
	}

	private async Task HandleAsGrpcAsync(HttpContext context, RequestDelegate next) {
		var (status, principal) = TrySelectProvider(context, out var authenticationRequest)
			? await authenticationRequest.AuthenticateAsync()
			: (HttpAuthenticationRequestStatus.Unauthenticated, default);

		GrpcProtocolHelpers.AddProtocolHeaders(context.Response);
		var trailersDestination = GrpcProtocolHelpers.GetTrailersDestination(context.Response);

		Status grpcStatus;
		switch (status) {
			case HttpAuthenticationRequestStatus.Authenticated:
				context.User = principal;
				await next(context);
				return;
			case HttpAuthenticationRequestStatus.Error:
				grpcStatus = new Status(StatusCode.Unknown, "Internal server error");
				break;
			case HttpAuthenticationRequestStatus.NotReady:
				// negative RetryPushbackHeader asks the grpc client not to retry the request.
				// the user is then explicit about what retries they want and aware of the delays incurred
				trailersDestination.Append(GrpcProtocolHelpers.RetryPushbackHeader, "-1");
				grpcStatus = new Status(StatusCode.Unavailable, "Server not ready");
				break;
			case HttpAuthenticationRequestStatus.Unauthenticated:
			default:
				grpcStatus = new Status(StatusCode.Unauthenticated, "Unauthenticated");
				break;
		}

		GrpcProtocolHelpers.SetStatus(trailersDestination, grpcStatus);

		// Immediately send remaining response content and trailers
		// If feature is null then reset/abort will still end request, but response won't have trailers
		var completionFeature = context.Features.Get<IHttpResponseBodyFeature>();
		if (completionFeature != null) {
			await completionFeature.CompleteAsync();
		}
	}

	private bool TrySelectProvider(HttpContext context, out HttpAuthenticationRequest authenticationRequest) {
		for (int i = 0; i < _httpAuthenticationProviders.Count; i++) {
			if (_httpAuthenticationProviders[i].Authenticate(context, out authenticationRequest)) {
				return true;
			}
		}

		authenticationRequest = default;
		return false;
	}

	private async Task AddHttp1ChallengeHeaders(HttpContext context) {
		context.Response.StatusCode = HttpStatusCode.Unauthorized;
		var authSchemes = _authenticationProvider.GetSupportedAuthenticationSchemes();
		// ReSharper disable once ConditionIsAlwaysTrueOrFalse
		if (authSchemes != null && authSchemes.Any()) {
			//add "X-" in front to prevent any default browser behaviour e.g Basic Auth popups
			context.Response.Headers.Append("WWW-Authenticate", $"X-{authSchemes.First()} realm=\"ESDB\"");
			var properties = _authenticationProvider.GetPublicProperties();
			// ReSharper disable once ConditionIsAlwaysTrueOrFalse
			if (properties != null && properties.Any()) {
				await context.Response.WriteAsync(JsonConvert.SerializeObject(properties));
			}
		}
	}
}
