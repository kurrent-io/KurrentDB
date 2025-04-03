// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.DataStructures;
using EventStore.Core.Messages;
using EventStore.Core.Services.Transport.Http.Messages;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;
using HttpStatusCode = EventStore.Transport.Http.HttpStatusCode;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Http;

internal class AuthenticatedHttpRequestProcessor : IHandle<HttpMessage.PurgeTimedOutRequests>, IHandle<AuthenticatedHttpRequestMessage> {
	private static readonly ILogger Log = Serilog.Log.ForContext<AuthenticatedHttpRequestProcessor>();

	private readonly PairingHeap<Tuple<DateTime, HttpEntityManager>> _pending = new((x, y) => x.Item1 < y.Item1);

	public void Handle(HttpMessage.PurgeTimedOutRequests message) {
		PurgeTimedOutRequests();
	}

	private void PurgeTimedOutRequests() {
		try {
			while (_pending.Count > 0) {
				var req = _pending.FindMin();
				if (req.Item1 <= DateTime.UtcNow || req.Item2.IsProcessing) {
					req = _pending.DeleteMin();
					req.Item2.ReplyStatus(HttpStatusCode.RequestTimeout,
						"Server was unable to handle request in time",
						e => Log.Debug(
							"Error occurred while closing timed out connection (HTTP service core): {e}.",
							e.Message));
				} else
					break;
			}
		} catch (Exception exc) {
			Log.Error(exc, "Error purging timed out requests in HTTP request processor.");
		}
	}

	public void Handle(AuthenticatedHttpRequestMessage message) {
		var manager = message.Manager;
		var match = message.Match;
		try {
			var reqParams = match.RequestHandler(manager, match.TemplateMatch);
			if (!reqParams.IsDone)
				_pending.Add(Tuple.Create(DateTime.UtcNow + reqParams.Timeout, manager));
		} catch (Exception exc) {
			Log.Error(exc, "Error while handling HTTP request '{url}'.", manager.HttpEntity.Request.Url);
			InternalServerError(manager);
		}

		PurgeTimedOutRequests();
	}

	private static void InternalServerError(HttpEntityManager entity) {
		entity.ReplyStatus(HttpStatusCode.InternalServerError, "Internal Server Error",
			e => Log.Debug("Error while closing HTTP connection (HTTP service core): {e}.", e.Message));
	}
}
