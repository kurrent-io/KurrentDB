// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Kurrent.Connectors.Webhook;
using Microsoft.AspNetCore.Http;

namespace KurrentDB.Connectors.Planes.Webhook;

internal static class WebhookHandler {
	public static async Task Ingest(HttpContext context, string connectorId, WebhookSourceRegistry registry) {
		if (!context.Request.HasJsonContentType()) {
			context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
			return;
		}

		if (!registry.TryGet(connectorId, out var source)) {
			context.Response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}

		var request = new WebhookRequest(
			Body:    await context.Request.ReadBodyAsync(context.RequestAborted),
			Headers: context.Request.GetHeaders(),
			Method:  context.Request.Method,
			Path:    context.Request.Path.Value ?? "",
			Query:   context.Request.GetQuery()
		);

		var result = await source.Receive(request, context.RequestAborted);

		context.Response.StatusCode = result switch {
			WebhookIngestionResult.SignatureFailed                        => StatusCodes.Status401Unauthorized,
			WebhookIngestionResult.Rejected                               => StatusCodes.Status400BadRequest,
			WebhookIngestionResult.RoutingError                           => StatusCodes.Status500InternalServerError,
			WebhookIngestionResult.Unavailable                            => StatusCodes.Status503ServiceUnavailable,
			WebhookIngestionResult.Accepted { Confirmation: null }        => StatusCodes.Status202Accepted,
			WebhookIngestionResult.Accepted { Confirmation: var pending } => MapConfirmation(await pending),
			_                                                             => StatusCodes.Status500InternalServerError
		};

		return;

		static int MapConfirmation(WriteConfirmation confirmation) => confirmation switch {
			WriteConfirmation.Confirmed => StatusCodes.Status201Created,
			WriteConfirmation.TimedOut  => StatusCodes.Status504GatewayTimeout,
			WriteConfirmation.Cancelled => StatusCodes.Status503ServiceUnavailable,
			_                           => throw new UnreachableException()
		};
	}
}

internal static class HttpRequestExtensions {
	extension(HttpRequest httpRequest) {
		public Dictionary<string, string> GetHeaders() =>
			httpRequest.Headers
				.Select(kvp => (kvp.Key, Value: kvp.Value.ToString()))
				.Where(kvp => !string.IsNullOrEmpty(kvp.Value))
				.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, string> GetQuery() =>
			httpRequest.Query
				.Select(kvp => (kvp.Key, Value: kvp.Value.ToString()))
				.Where(kvp => !string.IsNullOrEmpty(kvp.Value))
				.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

		public async Task<ReadOnlyMemory<byte>> ReadBodyAsync(CancellationToken ct) {
			using var ms = new MemoryStream();
			await httpRequest.Body.CopyToAsync(ms, ct);
			return ms.ToArray();
		}
	}
}
