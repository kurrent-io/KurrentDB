// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Transport.Http.Client;

public interface IHttpClient {
	void Get(string url, Action<HttpResponse> onSuccess, Action<Exception> onException);

	void Post(string url, string request, string contentType, Action<HttpResponse> onSuccess,
		Action<Exception> onException);

	void Delete(string url, Action<HttpResponse> onSuccess, Action<Exception> onException);

	void Put(string url, string request, string contentType, Action<HttpResponse> onSuccess,
		Action<Exception> onException);
}
