// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Security.Claims;
using EventStore.Common.Utils;
using EventStore.Transport.Http.Codecs;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace EventStore.Transport.Http.EntityManagement;

public class HttpEntity {
	public readonly HttpContext Context;
	private readonly bool _logHttpRequests;
	public readonly Action OnComplete;
	public readonly Uri RequestedUrl;
	public readonly Uri ResponseUrl;

	public readonly IHttpRequest Request;
	internal readonly IHttpResponse Response;
	public ClaimsPrincipal User => Context.User;

	public HttpEntity(HttpContext context, bool logHttpRequests, string advertiseAsHost, int advertiseAsPort,
		Action onComplete) {
		Context = context;
		Ensure.NotNull(context, nameof(context));
		Ensure.NotNull(onComplete, nameof(onComplete));

		var request = new CoreHttpRequestAdapter(context.Request);
		var response = new CoreHttpResponseAdapter(context.Response);
		_logHttpRequests = logHttpRequests;
		OnComplete = onComplete;
		RequestedUrl = BuildRequestedUrl(request, advertiseAsHost, advertiseAsPort);
		ResponseUrl = BuildRequestedUrl(request, advertiseAsHost, advertiseAsPort, true);
		Request = request;
		Response = response;
		Context = context;
	}

	public static Uri BuildRequestedUrl(IHttpRequest request,
		string advertiseAsAddress, int advertiseAsPort, bool overridePath = false) {
		var uriBuilder = new UriBuilder(request.Url);
		if (overridePath) {
			uriBuilder.Path = string.Empty;
		}

		if (advertiseAsAddress != null) {
			uriBuilder.Host = advertiseAsAddress.ToString();
		}

		if (advertiseAsPort > 0) {
			uriBuilder.Port = advertiseAsPort;
		}

		var forwardedPortHeaderValue = request.GetHeaderValues(ProxyHeaders.XForwardedPort);
		if (!StringValues.IsNullOrEmpty(forwardedPortHeaderValue)) {
			if (int.TryParse(forwardedPortHeaderValue.First(), out var requestPort)) {
				uriBuilder.Port = requestPort;
			}
		}

		var forwardedProtoHeaderValue = request.GetHeaderValues(ProxyHeaders.XForwardedProto);

		if (!StringValues.IsNullOrEmpty(forwardedProtoHeaderValue)) {
			uriBuilder.Scheme = forwardedProtoHeaderValue.First();
		}

		var forwardedHostHeaderValue = request.GetHeaderValues(ProxyHeaders.XForwardedHost);

		if (!StringValues.IsNullOrEmpty(forwardedHostHeaderValue)) {
			var host = forwardedHostHeaderValue.First();
			if (!string.IsNullOrEmpty(host)) {
				var parts = host.Split(new[] { ":" }, StringSplitOptions.RemoveEmptyEntries);
				uriBuilder.Host = parts[0];
				if (parts.Length > 1 && int.TryParse(parts[1], out var port)) {
					uriBuilder.Port = port;
				}
			}
		}

		var forwardedPrefixHeaderValue = request.GetHeaderValues(ProxyHeaders.XForwardedPrefix);
		if (!string.IsNullOrEmpty(forwardedPrefixHeaderValue)) {
			uriBuilder.Path = forwardedPrefixHeaderValue + uriBuilder.Path;
		}

		return uriBuilder.Uri;
	}

	public HttpEntityManager CreateManager(
		ICodec requestCodec, ICodec responseCodec, string[] allowedMethods, Action<HttpEntity> onRequestSatisfied) {
		return new HttpEntityManager(this, allowedMethods, onRequestSatisfied, requestCodec, responseCodec,
			_logHttpRequests, OnComplete);
	}

	public HttpEntityManager CreateManager()
		=> CreateManager(Codec.NoCodec, Codec.NoCodec, Array.Empty<string>(), _ => { });
}
