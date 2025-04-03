// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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
				new ICodec[] {Codec.ManualEncoding},
				new Operation(Operations.Node.Redirect)),
			(http, match) => http.ReplyTextContent(
				"Moved", 302, "Found", ContentType.PlainText,
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
