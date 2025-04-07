// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Utils;
using EventStore.Plugins.Authorization;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;

namespace EventStore.Core.Services.Transport.Http.Controllers;

public static class HttpHelpers {
	public static void RegisterRedirectAction(IHttpService service, string fromUrl, string toUrl) {
		service.RegisterAction(
			new ControllerAction(
				fromUrl,
				HttpMethod.Get,
				Codec.NoCodecs,
				new ICodec[] { Codec.ManualEncoding },
				new Operation(Operations.Node.Redirect)),
			(http, match) => http.ReplyTextContent(
				"Moved", 302, "Found", "text/plain",
				new[] {
					new KeyValuePair<string, string>(
						"Location", new Uri(match.BaseUri, toUrl).AbsoluteUri)
				}, Console.WriteLine));
	}

	public static void Reply(
		this HttpEntityManager http, string response, int code, string description, string contentType,
		IEnumerable<KeyValuePair<string, string>> headers = null) {
		http.Reply(Helper.UTF8NoBom.GetBytes(response), code, description, contentType, Helper.UTF8NoBom, headers,
			exception => { });
	}
}
