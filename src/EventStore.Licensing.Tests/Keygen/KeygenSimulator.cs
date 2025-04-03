// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using EventStore.Licensing.Keygen;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Text.Json;
using System.Text;
using System.Net;
using System;
using ContentType = EventStore.Transport.Http.ContentType;

namespace EventStore.Licensing.Tests.Keygen;

partial class KeygenSimulator : HttpMessageHandler {
	readonly Channel<HttpRequestMessage> _requests;
	readonly Channel<HttpResponseMessage> _responses;
	readonly JsonSerializerOptions _serializerOptions;
	readonly string _fingerprint = new Fingerprint(port: null).Get();

	public KeygenSimulator() {
		_requests = Channel.CreateUnbounded<HttpRequestMessage>();
		_responses = Channel.CreateUnbounded<HttpResponseMessage>();
		_serializerOptions = new JsonSerializerOptions {
			PropertyNameCaseInsensitive = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		};
	}

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken) {

		await _requests.Writer.WriteAsync(request, cancellationToken);
		return await _responses.Reader.ReadAsync(cancellationToken);
	}


	Task<HttpRequestMessage> Receive() => _requests.Reader.ReadAsync()
		.AsTask().WaitAsync(TimeSpan.FromSeconds(2));

	async Task Send<TResponse>(HttpStatusCode httpStatusCode, TResponse response) {
		var content = JsonSerializer.Serialize(response, _serializerOptions);
		await _responses.Writer.WriteAsync(new HttpResponseMessage {
			StatusCode = httpStatusCode,
			Content = new StringContent(content, Encoding.UTF8, ContentType.Json)
		});
	}
}
