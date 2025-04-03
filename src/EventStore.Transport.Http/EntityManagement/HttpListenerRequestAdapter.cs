// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;

namespace EventStore.Transport.Http.EntityManagement;

public class HttpListenerRequestAdapter : IHttpRequest {
	private readonly HttpListenerRequest _inner;

	public string[] AcceptTypes => _inner.AcceptTypes;
	public long ContentLength64 => _inner.ContentLength64;
	public string ContentType => _inner.ContentType;
	public string HttpMethod => _inner.HttpMethod;
	public Stream InputStream => _inner.InputStream;
	public string RawUrl => _inner.RawUrl;
	public IPEndPoint RemoteEndPoint => _inner.RemoteEndPoint;
	public Uri Url => _inner.Url;

	public HttpListenerRequestAdapter(HttpListenerRequest inner) {
		_inner = inner;
	}

	public IEnumerable<string> GetHeaderKeys() => _inner.Headers.AllKeys;
	public StringValues GetHeaderValues(string key) => _inner.Headers.GetValues(key);

	public IEnumerable<string> GetQueryStringKeys() => _inner.QueryString.AllKeys;
	public StringValues GetQueryStringValues(string key) => _inner.QueryString.GetValues(key);

}
