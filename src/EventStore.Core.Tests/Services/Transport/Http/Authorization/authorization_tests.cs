// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Tests.Integration;
using NUnit.Framework;
using ContentType = EventStore.Transport.Http.ContentType;

namespace EventStore.Core.Tests.Services.Transport.Http;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class Authorization<TLogFormat, TStreamId> : specification_with_cluster<TLogFormat, TStreamId> {
	private readonly Dictionary<string, HttpClient> _httpClients = new Dictionary<string, HttpClient>();
	private TimeSpan _timeout = TimeSpan.FromSeconds(5);
	private int _leaderId;


	private HttpClient CreateHttpClient(string username, string password) {
		var client = new HttpClient(new HttpClientHandler {
			AllowAutoRedirect = false,
			ServerCertificateCustomValidationCallback = delegate { return true; }
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
		var request = new HttpRequestMessage();
		request.Method = method;
		request.RequestUri = new Uri(url);

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
					string.Format("https://{0}/users/", _nodes[_leaderId].HttpEndPoint),
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

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		//find the leader node
		for (int i = 0; i < _nodes.Length; i++) {
			if (_nodes[i].NodeState == Data.VNodeState.Leader) {
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

	[Test, Combinatorial]
	public async Task authorization_tests(
		[Values(
			"None",
			"User",
			"Ops",
			"Admin"
		)] string userAuthorizationLevel,
		[Values(
			"/admin/shutdown;POST;Ops", /* this test is not executed for Ops and Admin to prevent the node from shutting down */
			"/admin/scavenge?startFromChunk={startFromChunk}&threads={threads};POST;Ops",
			"/admin/scavenge/{scavengeId};DELETE;Ops",
			"/admin/scavenge/current;GET;Ops",
			"/admin/scavenge/last;GET;Ops",
			"/admin/mergeindexes;POST;Ops",
			"/ping;GET;None",
			"/info;GET;None",
			"/info/options;GET;Ops",
			"/stats;GET;None",
			"/stats/replication;GET;None",
			"/stats/tcp;GET;None",
			"/stats/{*statPath};GET;None",
			"/streams/{stream};POST;None",
			"/streams/{stream};DELETE;None",
			"/streams/{stream}/incoming/{C38FC5D7-C0B6-4F47-A60D-9BF3D1B76BDF};POST;None",
			"/streams/{stream}/;POST;None",
			"/streams/{stream}/;DELETE;None",
			"/streams/{stream}/;GET;None",
			//These are not valid urls, and by default all users can perform all operations on streams
			//"/streams/{stream}?embed={embed};GET;User",
			//"/streams/{stream}/{event}?embed={embed};GET;User",
			//"/streams/{stream}/{event}/{count}?embed={embed};GET;User",
			//"/streams/{stream}/{event}/backward/{count}?embed={embed};GET;User",
			//"/streams/{stream}/metadata;POST;User",
			//"/streams/{stream}/metadata/;POST;User",
			//"/streams/{stream}/metadata?embed={embed};GET;User",
			//"/streams/{stream}/metadata/?embed={embed};GET;User",
			//"/streams/{stream}/metadata/{event}?embed={embed};GET;User",
			//"/streams/{stream}/metadata/{event}/{count}?embed={embed};GET;User",
			//"/streams/{stream}/metadata/{event}/backward/{count}?embed={embed};GET;User",
			"/streams/$all/;GET;User", /* only redirects, so "User" is allowed */
			"/streams/%24all/;GET;User", /* only redirects, so "User" is allowed */
			/* -- with default ACLs, only Admin should be able to read $all -- */
			"/streams/$all?embed={embed};GET;Admin",
			"/streams/$all/00000000000000000000000000000000/10?embed={embed};GET;Admin", /* /streams/$all/{position}/{count}?embed={embed} */
			"/streams/$all/head/backward/10?embed={embed};GET;Admin", /* /streams/$all/{position}/backward/{count}?embed={embed} */
			"/streams/%24all?embed={embed};GET;Admin",
			"/streams/%24all/00000000000000000000000000000000/10?embed={embed};GET;Admin", /* /streams/%24all/{position}/{count}?embed={embed} */
			"/streams/%24all/head/backward/10?embed={embed};GET;Admin", /* /streams/%24all/{position}/backward/{count}?embed={embed} */
			/* ------------------------------------------------------------- */
			"/subscriptions;GET;User",
			"/subscriptions/{stream};GET;User",
			"/subscriptions/{stream}/{subscription};PUT;Ops",
			"/subscriptions/{stream}/{subscription};POST;Ops",
			"/subscriptions/{stream}/{subscription};DELETE;Ops",
			"/subscriptions/{stream}/{subscription};GET;User",
			"/subscriptions/{stream}/{subscription}?embed={embed};GET;User",
			"/subscriptions/{stream}/{subscription}/{count}?embed={embed};GET;User",
			"/subscriptions/{stream}/{subscription}/info;GET;User",
			"/subscriptions/{stream}/{subscription}/ack/{messageid};POST;User",
			"/subscriptions/{stream}/{subscription}/nack/{messageid}?action={action};POST;User",
			"/subscriptions/{stream}/{subscription}/ack?ids={messageids};POST;User",
			"/subscriptions/{stream}/{subscription}/nack?ids={messageids}&action={action};POST;User",
			"/subscriptions/{stream}/{subscription}/replayParked?stopAt=1;POST;Ops",
			"/users;GET;Admin",
			"/users/;GET;Admin",
			"/users/{login};GET;Admin",
			"/users/$current;GET;User",
			"/users;POST;Admin",
			"/users/;POST;Admin",
			"/users/{login};PUT;Admin",
			"/users/{login};DELETE;Admin",
			"/users/{login}/command/enable;POST;Admin",
			"/users/{login}/command/disable;POST;Admin",
			"/users/{login}/command/reset-password;POST;Admin",
			//"/users/{login}/command/change-password;POST;User", Users can only change their own password, so this url won't be correct in these tests
			"/web/{*remaining_path};GET;None",
			";GET;None",
			"/web;GET;None"
		)] string httpEndpointDetails
	) {
		/*use the leader node endpoint to avoid any redirects*/
		var nodeEndpoint = _nodes[_leaderId].HttpEndPoint;
		var httpEndpointTokens = httpEndpointDetails.Split(';');
		var endpointUrl = httpEndpointTokens[0];
		var httpMethod = GetHttpMethod(httpEndpointTokens[1]);
		var requiredMinAuthorizationLevel = httpEndpointTokens[2];

		/* this test was done manually for Admin and Ops */
		if (endpointUrl == "/admin/shutdown" && (userAuthorizationLevel == "Admin" || userAuthorizationLevel == "Ops")) {
			return;
		}

		var url = $"https://{nodeEndpoint}{endpointUrl}";
		var body = GetData(httpMethod, endpointUrl);
		var contentType = httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put || httpMethod == HttpMethod.Delete ? ContentType.Json : null;
		var statusCode = await SendRequest(_httpClients[userAuthorizationLevel], httpMethod, url, body, contentType);

		if (GetAuthLevel(userAuthorizationLevel) >= GetAuthLevel(requiredMinAuthorizationLevel)) {
			Assert.AreNotEqual(401, statusCode);
		} else {
			if (statusCode >= 300 && statusCode < 400) {
				//Redirects are always allowed because authorization is done on the canonical url
				Assert.GreaterOrEqual(statusCode,300);
				Assert.LessOrEqual(statusCode, 307);
			} else {
				if (userAuthorizationLevel == "None") {
					Assert.GreaterOrEqual(statusCode, 401);
					Assert.LessOrEqual(statusCode, 403);
				} else {
					Assert.AreEqual(401, statusCode);
				}
			}
		}
	}

	private string GetData(HttpMethod httpMethod, string url) {
		if (httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put || httpMethod == HttpMethod.Delete) {
			if (url.Equals("/users/{login}/command/change-password") || url.Equals("/users/{login}/command/reset-password")) {
				return "{newPassword: \"changeit\"}";
			} else if (url.Equals("/users") || url.Equals("/users/")) {
				return "{loginName: \"test\", fullName: \"test\", password: \"changeit\", groups: []}";
			}
			return "{}";
		} else {
			return null;
		}
	}
}
