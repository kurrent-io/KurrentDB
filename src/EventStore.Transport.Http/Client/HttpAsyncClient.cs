// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using EventStore.Common.Utils;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;

namespace EventStore.Transport.Http.Client;

public class HttpAsyncClient : IHttpClient {
	private readonly HttpClient _client;

	static HttpAsyncClient() {
		ServicePointManager.MaxServicePointIdleTime = 10000;
		ServicePointManager.DefaultConnectionLimit = 500;
	}

	public HttpAsyncClient(TimeSpan timeout, HttpMessageHandler httpMessageHandler = null) {
		_client = httpMessageHandler == null ? new HttpClient() : new HttpClient(httpMessageHandler);
		_client.Timeout = timeout;
	}

	public void Get(string url, Action<HttpResponse> onSuccess, Action<Exception> onException) {
		Get(url, null, onSuccess, onException);
	}

	public void Get(string url, IEnumerable<KeyValuePair<string, string>> headers, Action<HttpResponse> onSuccess,
		Action<Exception> onException) {
		Ensure.NotNull(url, "url");
		Ensure.NotNull(onSuccess, "onSuccess");
		Ensure.NotNull(onException, "onException");

		Receive(HttpMethod.Get, url, headers, onSuccess, onException);
	}

	public void Post(string url, string body, string contentType, Action<HttpResponse> onSuccess,
		Action<Exception> onException) {
		Post(url, body, contentType, null, onSuccess, onException);
	}

	public void Post(string url, string body, string contentType, IEnumerable<KeyValuePair<string, string>> headers,
		Action<HttpResponse> onSuccess, Action<Exception> onException) {
		Ensure.NotNull(url, "url");
		Ensure.NotNull(body, "body");
		Ensure.NotNull(contentType, "contentType");
		Ensure.NotNull(onSuccess, "onSuccess");
		Ensure.NotNull(onException, "onException");

		Send(HttpMethod.Post, url, body, contentType, headers, onSuccess, onException);
	}

	public void Delete(string url, Action<HttpResponse> onSuccess, Action<Exception> onException) {
		Ensure.NotNull(url, "url");
		Ensure.NotNull(onSuccess, "onSuccess");
		Ensure.NotNull(onException, "onException");

		Receive(HttpMethod.Delete, url, null, onSuccess, onException);
	}

	public void Put(string url, string body, string contentType, Action<HttpResponse> onSuccess,
		Action<Exception> onException) {
		Ensure.NotNull(url, "url");
		Ensure.NotNull(body, "body");
		Ensure.NotNull(contentType, "contentType");
		Ensure.NotNull(onSuccess, "onSuccess");
		Ensure.NotNull(onException, "onException");

		Send(HttpMethod.Put, url, body, contentType, null, onSuccess, onException);
	}

	public void Dispose() {
		_client.Dispose();
	}

	private void Receive(string method, string url, IEnumerable<KeyValuePair<string, string>> headers,
		Action<HttpResponse> onSuccess, Action<Exception> onException) {
		var request = new HttpRequestMessage();
		request.Method = new System.Net.Http.HttpMethod(method);
		request.RequestUri = new Uri(url);

		if (headers != null) {
			foreach (var header in headers) {
				request.Headers.Add(header.Key, header.Value);
			}
		}

		Task.Run(() => SendRequest(request, onSuccess, onException));
	}

	private void Send(string method, string url, string body, string contentType,
		IEnumerable<KeyValuePair<string, string>> headers,
		Action<HttpResponse> onSuccess, Action<Exception> onException) {
		var bodyBytes = Helper.UTF8NoBom.GetBytes(body);
		var stream = new MemoryStream(bodyBytes);
		var request = new HttpRequestMessage {
			Method = new System.Net.Http.HttpMethod(method),
			RequestUri = new Uri(url),
			Content = new StreamContent(stream) {
				Headers = {
					ContentType = new MediaTypeHeaderValue(contentType),
					ContentLength = bodyBytes.Length
				}
			}
		};

		if (headers != null) {
			foreach (var (key, value) in headers) {
				request.Headers.Add(key, value);
			}
		}

		Task.Run(() => SendRequest(request, onSuccess, onException));
	}

	private async Task SendRequest(HttpRequestMessage request, Action<HttpResponse> onSuccess, Action<Exception> onException) {
		try {
			using var response = await _client.SendAsync(request);
			onSuccess(new HttpResponse(response) {
				Body = await response.Content.ReadAsStringAsync()
			});
		} catch (Exception ex) {
			onException(ex);
			throw;
		} finally {
			request.Dispose();
		}
	}
}
