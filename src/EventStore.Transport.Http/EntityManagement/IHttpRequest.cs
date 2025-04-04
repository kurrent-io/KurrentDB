// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Microsoft.Extensions.Primitives;

namespace EventStore.Transport.Http.EntityManagement;

public interface IHttpRequest {
	string[] AcceptTypes { get; }
	long ContentLength64 { get; }
	string ContentType { get; }
	string HttpMethod { get; }
	Stream InputStream { get; }
	string RawUrl { get; }
	IPEndPoint RemoteEndPoint { get; }
	Uri Url { get; }
	IEnumerable<string> GetHeaderKeys();
	StringValues GetHeaderValues(string key);
	IEnumerable<string> GetQueryStringKeys();
	StringValues GetQueryStringValues(string key);
}
