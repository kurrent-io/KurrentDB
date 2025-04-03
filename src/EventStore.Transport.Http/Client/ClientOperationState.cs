// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net.Http;
using EventStore.Common.Utils;

namespace EventStore.Transport.Http.Client;

public class ClientOperationState {
	public readonly HttpRequestMessage Request;
	public readonly Action<HttpResponse> OnSuccess;
	public readonly Action<Exception> OnError;

	public HttpResponse Response { get; set; }

	public ClientOperationState(HttpRequestMessage request, Action<HttpResponse> onSuccess,
		Action<Exception> onError) {
		Ensure.NotNull(request, "request");
		Ensure.NotNull(onSuccess, "onSuccess");
		Ensure.NotNull(onError, "onError");

		Request = request;
		OnSuccess = onSuccess;
		OnError = onError;
	}

	public void Dispose() {
		Request.Dispose();
	}
}
