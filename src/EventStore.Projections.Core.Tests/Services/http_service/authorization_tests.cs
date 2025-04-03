// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Tests.ClientAPI.Cluster;
using NUnit.Framework;
using ContentType = EventStore.Transport.Http.ContentType;

namespace EventStore.Projections.Core.Tests.Services.Transport.Http;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class Authorization<TLogFormat, TStreamId> : specification_with_standard_projections_runnning<TLogFormat, TStreamId> {
	private Dictionary<string, HttpClient> _httpClients = new Dictionary<string, HttpClient>();
	private TimeSpan _timeout = TimeSpan.FromSeconds(10);
	private int _leaderId;

	private HttpClient CreateHttpClient(string username, string password) {
		var client = new HttpClient(new HttpClientHandler {
			AllowAutoRedirect = false
		}) {
			Timeout = _timeout
		};
		if (!string.IsNullOrEmpty(username)) {
			client.DefaultRequestHeaders.Authorization =
				new AuthenticationHeaderValue(
					"Basic", System.Convert.ToBase64String(
						System.Text.Encoding.ASCII.GetBytes(
							$"{username}:{password}")));
		}

		return client;
	}

	private async Task<int> SendRequest(HttpClient client, HttpMethod method, string url, string body, string contentType) {
		using var request = new HttpRequestMessage {Method = method, RequestUri = new Uri(url)};

		if (body != null) {
			var bodyBytes = Helper.UTF8NoBom.GetBytes(body);
			var stream = new MemoryStream(bodyBytes);
			var content = new StreamContent(stream);
			content.Headers.ContentLength = bodyBytes.Length;
			if (contentType != null)
				content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
			request.Content = content;
		}

		var result = await client.SendAsync(request);
		return (int)result.StatusCode;
	}

	private HttpMethod GetHttpMethod(string method) {
		switch (method) {
			case "GET":
				return HttpMethod.Get;
			case "POST":
				return HttpMethod.Post;
			case "PUT":
				return HttpMethod.Put;
			case "DELETE":
				return HttpMethod.Delete;
			default:
				throw new Exception("Unknown Http Method");
		}
	}

	private int GetAuthLevel(string userAuthorizationLevel) {
		switch (userAuthorizationLevel) {
			case "None":
				return 0;
			case "User":
				return 1;
			case "Ops":
				return 2;
			case "Admin":
				return 3;
			default:
				throw new Exception("Unknown authorization level");
		}
	}
	public async Task CreateUser(string username, string password) {
		for (int trial = 1; trial <= 5; trial++) {
			try {
				var dataStr = string.Format("{{loginName: '{0}', fullName: '{1}', password: '{2}', groups: []}}", username, username, password);
				var data = Helper.UTF8NoBom.GetBytes(dataStr);
				var stream = new MemoryStream(data);
				var content = new StreamContent(stream);
				content.Headers.Add("Content-Type", ContentType.Json);

				var res = await _httpClients["Admin"].PostAsync(
					string.Format("http://{0}/users/", _nodes[_leaderId].HttpEndPoint),
					content
				);
				res.EnsureSuccessStatusCode();
				break;
			} catch (HttpRequestException) {
				if (trial == 5) {
					throw new Exception(string.Format("Error creating user: {0}", username));
				}
				await Task.Delay(1000);
			}
		}
	}

	protected override async Task Given() {
		await base.Given();
		//find the leader node
		for (int i = 0; i < _nodes.Length; i++) {
			if (_nodes[i].NodeState == EventStore.Core.Data.VNodeState.Leader) {
				_leaderId = i;
				break;
			}
		}

		_httpClients["Admin"] = CreateHttpClient("admin", "changeit");
		_httpClients["Ops"] = CreateHttpClient("ops", "changeit");
		await CreateUser("user", "changeit");
		_httpClients["User"] = CreateHttpClient("user", "changeit");
		_httpClients["None"] = CreateHttpClient(null, null);
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		foreach (var kvp in _httpClients) {
			kvp.Value.Dispose();
		}
		return base.TestFixtureTearDown();
	}
	//just using Ignore not being honored for Combinatorial tests in the dotnet test runner
	//[Test, Combinatorial, Ignore("Invalid URl templates, retained for future refactoring")]
	public async Task authorization_tests(
		[Values(
			"None",
			"User",
			"Ops",
			"Admin"
		)] string userAuthorizationLevel,
		[Values(
			"/web/es/js/projections/{*remaining_path};GET;None",
			"/web/es/js/projections/v8/Prelude/{*remaining_path};GET;None",
			"/web/projections;GET;None",
			"/projections;GET;User",
			"/projections/any;GET;User",
			"/projections/all-non-transient;GET;User",
			"/projections/transient;GET;User",
			"/projections/onetime;GET;User",
			"/projections/continuous;GET;User",
			"/projections/transient?name=name&type=type&enabled={enabled};POST;User", /* /projections/transient?name={name}&type={type}&enabled={enabled} */
			"/projections/onetime?name=name&type=type&enabled={enabled}&checkpoints={checkpoints}&emit={emit}&trackemittedstreams={trackemittedstreams};POST;Ops", /* /projections/onetime?name={name}&type={type}&enabled={enabled}&checkpoints={checkpoints}&emit={emit}&trackemittedstreams={trackemittedstreams} */
			"/projections/continuous?name=name&type=type&enabled={enabled}&emit={emit}&trackemittedstreams={trackemittedstreams};POST;Ops", /* /projections/continuous?name={name}&type={type}&enabled={enabled}&emit={emit}&trackemittedstreams={trackemittedstreams} */
			"/projection/name/query?config={config};GET;User", /* /projection/{name}/query?config={config} */
			"/projection/name/query?type={type}&emit={emit};PUT;User", /* /projection/{name}/query?type={type}&emit={emit} */
			"/projection/name;GET;User", /* /projection/{name} */
			"/projection/name?deleteStateStream={deleteStateStream}&deleteCheckpointStream={deleteCheckpointStream}&deleteEmittedStreams={deleteEmittedStreams};DELETE;Ops", /* /projection/{name}?deleteStateStream={deleteStateStream}&deleteCheckpointStream={deleteCheckpointStream}&deleteEmittedStreams={deleteEmittedStreams} */
			"/projection/name/statistics;GET;User", /* projection/{name}/statistics */
			"/projections/read-events;POST;User",
			"/projection/{name}/state?partition={partition};GET;User",
			"/projection/{name}/result?partition={partition};GET;User",
			"/projection/{name}/command/disable?enableRunAs={enableRunAs};POST;User",
			"/projection/{name}/command/enable?enableRunAs={enableRunAs};POST;User",
			"/projection/{name}/command/reset?enableRunAs={enableRunAs};POST;User",
			"/projection/{name}/command/abort?enableRunAs={enableRunAs};POST;User",
			"/projection/{name}/config;GET;Ops",
			"/projection/{name}/config;PUT;Ops"
			/*"/sys/subsystems;GET;Ops"*/ /* this endpoint has been commented since this controller is not registered when using a MiniNode */
		)] string httpEndpointDetails
	) {
		/*use the leader node endpoint to avoid any redirects*/
		var nodeEndpoint = _nodes[_leaderId].HttpEndPoint;
		var httpEndpointTokens = httpEndpointDetails.Split(';');
		var endpointUrl = httpEndpointTokens[0];
		var httpMethod = GetHttpMethod(httpEndpointTokens[1]);
		var requiredMinAuthorizationLevel = httpEndpointTokens[2];

		var url = $"http://{nodeEndpoint}{endpointUrl}";
		var body = GetData(httpMethod, endpointUrl);
		var contentType = httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put || httpMethod == HttpMethod.Delete ? ContentType.Json : null;
		var statusCode = await SendRequest(_httpClients[userAuthorizationLevel], httpMethod, url, body, contentType);

		if (GetAuthLevel(userAuthorizationLevel) >= GetAuthLevel(requiredMinAuthorizationLevel)) {
			Assert.AreNotEqual(401, statusCode);
		} else {
			Assert.AreEqual(401, statusCode);
		}
	}

	private string GetData(HttpMethod httpMethod, string url) {
		if (httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put || httpMethod == HttpMethod.Delete) {
			return "{}";
		} else {
			return null;
		}
	}
}
