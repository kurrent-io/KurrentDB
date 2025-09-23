// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Tests.ClientAPI.Cluster;
using NUnit.Framework;
using ContentType = KurrentDB.Transport.Http.ContentType;

namespace KurrentDB.Projections.Core.Tests.Services.http_service;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class Authorization<TLogFormat, TStreamId> : specification_with_standard_projections_runnning<TLogFormat, TStreamId> {
	private readonly Dictionary<string, HttpClient> _httpClients = new();
	private readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
	private int _leaderId;

	private HttpClient CreateHttpClient(string username, string password) {
		var client = new HttpClient(new HttpClientHandler {
			AllowAutoRedirect = false
		}) {
			Timeout = _timeout
		};
		if (!string.IsNullOrEmpty(username)) {
			client.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}")));
		}

		return client;
	}

	private static async Task<int> SendRequest(HttpClient client, HttpMethod method, string url, string body, string contentType) {
		using var request = new HttpRequestMessage();
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

	private static HttpMethod GetHttpMethod(string method) {
		return method switch {
			"GET" => HttpMethod.Get,
			"POST" => HttpMethod.Post,
			"PUT" => HttpMethod.Put,
			"DELETE" => HttpMethod.Delete,
			_ => throw new Exception("Unknown Http Method")
		};
	}

	private static int GetAuthLevel(string userAuthorizationLevel) {
		return userAuthorizationLevel switch {
			"None" => 0,
			"User" => 1,
			"Ops" => 2,
			"Admin" => 3,
			_ => throw new Exception("Unknown authorization level")
		};
	}

	private async Task CreateUser(string username, string password) {
		for (int trial = 1; trial <= 5; trial++) {
			try {
				var dataStr = $"{{loginName: '{username}', fullName: '{username}', password: '{password}', groups: []}}";
				var data = Helper.UTF8NoBom.GetBytes(dataStr);
				var stream = new MemoryStream(data);
				var content = new StreamContent(stream);
				content.Headers.Add("Content-Type", ContentType.Json);

				var res = await _httpClients["Admin"].PostAsync(
					$"http://{_nodes[_leaderId].HttpEndPoint}/users/",
					content
				);
				res.EnsureSuccessStatusCode();
				break;
			} catch (HttpRequestException) {
				if (trial == 5) {
					throw new Exception($"Error creating user: {username}");
				}

				await Task.Delay(1000);
			}
		}
	}

	protected override async Task Given() {
		await base.Given();
		//find the leader node
		for (int i = 0; i < _nodes.Length; i++) {
			if (_nodes[i].NodeState == VNodeState.Leader) {
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
}
