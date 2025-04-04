// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;

namespace EventStore.Transport.Http;

public interface ICodec {
	string ContentType { get; }
	Encoding Encoding { get; }
	bool CanParse(MediaType format);
	bool SuitableForResponse(MediaType component);
	bool HasEventIds { get; }
	bool HasEventTypes { get; }
	T From<T>(string text);
	string To<T>(T value);
}
