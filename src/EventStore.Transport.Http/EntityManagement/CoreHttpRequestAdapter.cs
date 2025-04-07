// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace EventStore.Transport.Http.EntityManagement;

public class CoreHttpRequestAdapter : IHttpRequest {
	private readonly HttpRequest _inner;

	public CoreHttpRequestAdapter(HttpRequest inner) {
		_inner = inner;
	}

	public string[] AcceptTypes => _inner.Headers.GetCommaSeparatedValues("accept");

	public long ContentLength64 => _inner.ContentLength ?? 0;

	public string ContentType => _inner.ContentType;

	public string HttpMethod => _inner.Method;

	public Stream InputStream => _inner.Body;

	public string RawUrl => _inner.Path + (_inner.QueryString.HasValue ? _inner.QueryString.Value : null);

	public IPEndPoint RemoteEndPoint => _inner.HttpContext.Connection.RemoteIpAddress != null ? new IPEndPoint(
		_inner.HttpContext.Connection.RemoteIpAddress, _inner.HttpContext.Connection.RemotePort) : null;

	public Uri Url => new Uri(new Uri($"{_inner.Scheme}://{_inner.Host}"), RawUrl);

	public IEnumerable<string> GetQueryStringKeys() => _inner.Query.Keys;

	public StringValues GetQueryStringValues(string key) => _inner.Query[key];

	public IEnumerable<string> GetHeaderKeys() => _inner.Headers.Keys;
	public StringValues GetHeaderValues(string key) => _inner.Headers[key];
}
