// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.Transport.Http;
using EventStore.Core.Settings;
using EventStore.Transport.Http;
using EventStore.Transport.Http.EntityManagement;
using HttpStatusCode = EventStore.Transport.Http.HttpStatusCode;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services;

public class HttpSendService : IHttpForwarder,
	IHandle<SystemMessage.StateChangeMessage>,
	IHandle<HttpMessage.HttpSend> {
	private static readonly ILogger Log = Serilog.Log.ForContext<HttpSendService>();

	private readonly HttpMessagePipe _httpPipe;
	private readonly bool _forwardRequests;
	private readonly HttpClient _forwardClient;
	private MemberInfo _leaderInfo;

	public HttpSendService(HttpMessagePipe httpPipe, bool forwardRequests, CertificateDelegates.ServerCertificateValidator externServerCertValidator) {
		_httpPipe = Ensure.NotNull(httpPipe);
		_forwardRequests = forwardRequests;

		var socketsHttpHandler = new SocketsHttpHandler {
			SslOptions = {
				CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
				RemoteCertificateValidationCallback = (_, certificate, chain, errors) => {
					var (isValid, error) = externServerCertValidator(certificate, chain, errors, _leaderInfo?.HttpEndPoint.GetOtherNames());
					if (!isValid && error != null) {
						Log.Error("Server certificate validation error: {e}", error);
					}

					return isValid;
				}
			},
			PooledConnectionLifetime = ESConsts.HttpClientConnectionLifeTime
		};
		_forwardClient = new HttpClient(socketsHttpHandler);
	}

	public void Handle(SystemMessage.StateChangeMessage message) {
		switch (message.State) {
			case VNodeState.PreReplica:
			case VNodeState.CatchingUp:
			case VNodeState.Clone:
			case VNodeState.Follower:
			case VNodeState.PreReadOnlyReplica:
			case VNodeState.ReadOnlyReplica:
				_leaderInfo = ((SystemMessage.ReplicaStateMessage)message).Leader;
				break;
			case VNodeState.Initializing:
			case VNodeState.DiscoverLeader:
			case VNodeState.Unknown:
			case VNodeState.PreLeader:
			case VNodeState.Leader:
			case VNodeState.ResigningLeader:
			case VNodeState.Manager:
			case VNodeState.ShuttingDown:
			case VNodeState.Shutdown:
			case VNodeState.ReadOnlyLeaderless:
				_leaderInfo = null;
				break;
			default:
				throw new Exception($"Unknown node state: {message.State}.");
		}
	}

	public void Handle(HttpMessage.HttpSend message) {
		if (message.Message is HttpMessage.DeniedToHandle deniedToHandle) {
			int code;
			switch (deniedToHandle.Reason) {
				case DenialReason.ServerTooBusy:
					code = HttpStatusCode.ServiceUnavailable;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			// todo: histogram metric?
			message.HttpEntityManager.ReplyStatus(
				code,
				deniedToHandle.Details,
				exc => Log.Debug("Error occurred while replying to HTTP with message {message}: {e}.",
					message.Message, exc.Message));
		} else {
			var response = message.Data;
			var config = message.Configuration;

			// todo: histogram metric?
			if (response is byte[] bytes) {
				message.HttpEntityManager.ReplyContent(
					bytes,
					config.Code,
					config.Description,
					config.ContentType,
					config.Headers,
					exc => Log.Debug("Error occurred while replying to HTTP with message {message}: {e}.",
						message.Message, exc.Message));
			} else {
				message.HttpEntityManager.ReplyTextContent(
					response as string,
					config.Code,
					config.Description,
					config.ContentType,
					config.Headers,
					exc => Log.Debug("Error occurred while replying to HTTP with message {message}: {e}.",
						message.Message, exc.Message));
			}
		}
	}

	bool IHttpForwarder.ForwardRequest(HttpEntityManager manager) {
		var leaderInfo = _leaderInfo;
		if (!_forwardRequests || leaderInfo == null) return false;

		var srcUrl = manager.RequestedUrl;
		var srcBase = new Uri($"{srcUrl.Scheme}://{srcUrl.Host}:{srcUrl.Port}/", UriKind.Absolute);
		var baseUri = new Uri(leaderInfo.HttpEndPoint.ToHttpUrl(srcUrl.Scheme));
		var forwardUri = new Uri(baseUri, srcBase.MakeRelativeUri(srcUrl));
		ForwardRequest(manager, forwardUri);
		return true;
	}

	private void ForwardRequest(HttpEntityManager manager, Uri forwardUri) {
		var srcReq = manager.HttpEntity.Request;
		var request = new HttpRequestMessage();
		request.RequestUri = forwardUri;
		request.Method = new System.Net.Http.HttpMethod(srcReq.HttpMethod);

		var hasContentLength = false;
		// Copy unrestricted headers (including cookies, if any)
		foreach (var headerKey in srcReq.GetHeaderKeys()) {
			try {
				switch (headerKey.ToLower()) {
					case "connection":
					case "content-type":
					case "expect":
					case "proxy-connection":
					case "range":
						break;
					case "accept":
						request.Headers.Accept.ParseAdd(srcReq.GetHeaderValues(headerKey).ToString());
						break;
					case "content-length":
						hasContentLength = true;
						break;
					case "date":
						request.Headers.Date = DateTime.Parse(srcReq.GetHeaderValues(headerKey).ToString());
						break;
					case "host":
						request.Headers.Host = $"{forwardUri.Host}:{forwardUri.Port}";
						break;
					case "if-modified-since":
						request.Headers.IfModifiedSince = DateTime.Parse(srcReq.GetHeaderValues(headerKey).ToString());
						break;
					case "referer":
						request.Headers.Referrer = new Uri(srcReq.GetHeaderValues(headerKey).ToString());
						break;
					case "transfer-encoding":
						request.Headers.TransferEncoding.ParseAdd(srcReq.GetHeaderValues(headerKey).ToString());
						break;
					case "user-agent":
						request.Headers.UserAgent.ParseAdd(srcReq.GetHeaderValues(headerKey).ToString());
						break;

					default:
						request.Headers.Add(headerKey, srcReq.GetHeaderValues(headerKey).ToString());
						break;
				}
			} catch (FormatException) {
				request.Headers.TryAddWithoutValidation(headerKey, srcReq.GetHeaderValues(headerKey).ToString());
			}
		}

		if (!request.Headers.Contains(ProxyHeaders.XForwardedHost)) {
			request.Headers.Add(ProxyHeaders.XForwardedHost, $"{manager.RequestedUrl.Host}:{manager.RequestedUrl.Port}");
		}

		// Copy content (if content body is allowed)
		if (!string.Equals(srcReq.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
		    && !string.Equals(srcReq.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase)
		    && hasContentLength) {
			var streamContent = new StreamContent(srcReq.InputStream);
			streamContent.Headers.ContentLength = srcReq.ContentLength64;
			request.Content = streamContent;

			if (MediaTypeHeaderValue.TryParse(srcReq.ContentType, out var contentType)) {
				streamContent.Headers.ContentType = contentType;
			}
		}

		ForwardResponse(manager, request);
	}

	private static void ForwardReplyFailed(HttpEntityManager manager) {
		manager.ReplyStatus(HttpStatusCode.InternalServerError, "Error while forwarding request", _ => { });
	}

	private void ForwardResponse(HttpEntityManager manager, HttpRequestMessage request) {
		_forwardClient.SendAsync(request)
			.ContinueWith(t => {
				HttpResponseMessage response;
				try {
					response = t.Result;
				} catch (Exception ex) {
					Log.Debug("Error in SendAsync for forwarded request for '{requestedUrl}': {e}.", manager.RequestedUrl, ex.InnerException.Message);
					ForwardReplyFailed(manager);
					return;
				}

				manager.ForwardReply(response, exc => Log.Debug("Error forwarding response for '{requestedUrl}': {e}.", manager.RequestedUrl, exc.Message));
			});
	}
}
