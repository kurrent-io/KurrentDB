// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Api.Streams.Authorization;

/// <summary>
/// Defines the various operations that can be performed on a stream for authorization purposes.
/// </summary>
public enum StreamOperation {
	Read,
	Write,
	Delete,
	MetadataRead,
	MetadataWrite
}
