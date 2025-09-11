// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Api.Streams;

public record SchemaInfo(string Name, SchemaFormat Format, string? Id = null) {
	public bool HasId      => !string.IsNullOrWhiteSpace(Id);
	public bool IsJson     => Format == SchemaFormat.Json;
	public bool IsProtobuf => Format == SchemaFormat.Protobuf;
	public bool IsBytes    => Format == SchemaFormat.Bytes;
}
