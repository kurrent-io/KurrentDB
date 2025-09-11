// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Kurrent.Protobuf;

[PublicAPI]
public static class StructEncoding {
	public static ReadOnlyMemory<byte> GetJsonBytes(Struct source) =>
		Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(source));

	public static Struct ParseJsonBytes(ReadOnlySpan<byte> source) =>
		JsonParser.Default.Parse<Struct>(Encoding.UTF8.GetString(source));

	public static Struct ParseJsonBytes(ReadOnlyMemory<byte> source) =>
		ParseJsonBytes(source.Span);
}
