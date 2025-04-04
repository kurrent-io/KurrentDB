// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.IO;

namespace EventStore.Transport.Http.EntityManagement;

public interface IHttpResponse {
	void AddHeader(string name, string value);
	void Close();
	long ContentLength64 { get; set; }
	string ContentType { get; set; }
	Stream OutputStream { get; }
	int StatusCode { get; set; }
	string StatusDescription { get; set; }
}
