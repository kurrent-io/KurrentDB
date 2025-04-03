// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Net;
using System.Text;

namespace EventStore.Transport.Http.EntityManagement;

public class HttpListenerResponseAdapter : IHttpResponse {
	private readonly HttpListenerResponse _inner;

	public void AddHeader(string name, string value) => _inner.AddHeader(name, value);

	public void Close() => _inner.Close();

	public Encoding ContentEncoding {
		get => _inner.ContentEncoding;
		set => _inner.ContentEncoding = value;
	}

	public long ContentLength64 {
		get => _inner.ContentLength64;
		set => _inner.ContentLength64 = value;
	}

	public string ContentType {
		get => _inner.ContentType;
		set => _inner.ContentType = value;
	}

	public Stream OutputStream => _inner.OutputStream;

	public int StatusCode {
		get => _inner.StatusCode;
		set => _inner.StatusCode = value;
	}

	public string StatusDescription {
		get => _inner.StatusDescription;
		set => _inner.StatusDescription = value;
	}

	public HttpListenerResponseAdapter(HttpListenerResponse inner) {
		_inner = inner;
	}
}
