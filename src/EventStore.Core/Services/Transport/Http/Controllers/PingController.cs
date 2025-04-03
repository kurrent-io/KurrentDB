// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Plugins.Authorization;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Http.Controllers;

public class PingController : IHttpController {
	private static readonly ILogger Log = Serilog.Log.ForContext<PingController>();

	private static readonly ICodec[] SupportedCodecs = new ICodec[]
		{Codec.Json, Codec.Xml, Codec.ApplicationXml, Codec.Text};

	public void Subscribe(IHttpService service) {
		Ensure.NotNull(service, "service");
		service.RegisterAction(new ControllerAction("/ping", HttpMethod.Get, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Node.Ping)),
			OnGetPing);
	}

	private void OnGetPing(HttpEntityManager entity, UriTemplateMatch match) {
		var response = new HttpMessage.TextMessage("Ping request successfully handled");
		entity.ReplyTextContent(Format.TextMessage(entity, response),
			HttpStatusCode.OK,
			"OK",
			entity.ResponseCodec.ContentType,
			null,
			e => Log.Error(e, "Error while writing HTTP response (ping)"));
	}
}
