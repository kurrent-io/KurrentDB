// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using EventStore.Core.Bus;
using EventStore.Core.Services.Transport.Http;
using EventStore.Core.Services.Transport.Http.Controllers;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;
using EventStore.Common.Utils;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Tests.Http;

public class TestController : CommunicationController {
	public TestController(IPublisher publisher)
		: base(publisher) {
	}

	protected override void SubscribeCore(IHttpService service) {
		Register(service, "/test1", Test1Handler);
		Register(service, "/test-anonymous", TestAnonymousHandler);
		Register(service, "/test-encoding/{a}?b={b}", TestEncodingHandler);
		Register(service, "/test-encoding-reserved-%20?b={b}",
			(manager, match) => TestEncodingHandler(manager, match, "%20"));
		Register(service, "/test-encoding-reserved-%24?b={b}",
			(manager, match) => TestEncodingHandler(manager, match, "%24"));
		Register(service, "/test-encoding-reserved-%25?b={b}",
			(manager, match) => TestEncodingHandler(manager, match, "%25"));
		Register(service, "/test-encoding-reserved- ?b={b}",
			(manager, match) => TestEncodingHandler(manager, match, " "));
		Register(service, "/test-encoding-reserved-$?b={b}",
			(manager, match) => TestEncodingHandler(manager, match, "$"));
		Register(service, "/test-encoding-reserved-%?b={b}",
			(manager, match) => TestEncodingHandler(manager, match, "%"));
		Register(service, "/test-timeout?sleepfor={sleepfor}",
			(manager, match) => TestTimeoutHandler(manager, match));
	}

	private void Register(
		IHttpService service, string uriTemplate, Action<HttpEntityManager, UriTemplateMatch> handler,
		string httpMethod = HttpMethod.Get) {
		Register(service, uriTemplate, httpMethod, handler, Codec.NoCodecs, new ICodec[] {Codec.ManualEncoding}, new Operation(Operations.Node.StaticContent));
	}

	private void Test1Handler(HttpEntityManager http, UriTemplateMatch match) {
		if (http.User != null && !http.User.HasClaim(ClaimTypes.Anonymous, ""))
			http.Reply("OK", 200, "OK", "text/plain");
		else
			http.Reply("Please authenticate yourself", 401, "Unauthorized", "text/plain");
	}

	private void TestAnonymousHandler(HttpEntityManager http, UriTemplateMatch match) {
		if (!http.User.HasClaim(ClaimTypes.Anonymous, ""))
			http.Reply("ERROR", 500, "ERROR", "text/plain");
		else
			http.Reply("OK", 200, "OK", "text/plain");
	}

	private void TestEncodingHandler(HttpEntityManager http, UriTemplateMatch match) {
		var a = match.BoundVariables["a"];
		var b = match.BoundVariables["b"];

		http.Reply(new {a = a, b = b, rawSegment = http.RequestedUrl.Segments[2]}.ToJson(), 200, "OK",
			ContentType.Json);
	}

	private void TestEncodingHandler(HttpEntityManager http, UriTemplateMatch match, string a) {
		var b = match.BoundVariables["b"];

		http.Reply(
			new {
				a = a,
				b = b,
				rawSegment = http.RequestedUrl.Segments[1],
				requestUri = match.RequestUri,
				rawUrl = http.HttpEntity.Request.RawUrl
			}.ToJson(), 200, "OK", ContentType.Json);
	}

	private void TestTimeoutHandler(HttpEntityManager http, UriTemplateMatch match) {
		var sleepFor = int.Parse(match.BoundVariables["sleepfor"]);
		System.Threading.Thread.Sleep(sleepFor);
		http.Reply("OK", 200, "OK", "text/plain");
	}
}
