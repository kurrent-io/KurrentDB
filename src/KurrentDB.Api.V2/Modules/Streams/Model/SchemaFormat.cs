// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Api.Streams;

/// <summary>
/// Specifies the data format for schema content.
/// </summary>
public enum SchemaFormat {
    /// <summary>
    /// Default value indicating that the data format is unspecified.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The data is in JSON format.
    /// </summary>
    Json = 1,

    /// <summary>
    /// The data is in Protocol Buffers format.
    /// </summary>
    Protobuf = 2,

    /// <summary>
    /// The data is in Avro format.
    /// </summary>
    Avro = 3,

    /// <summary>
    /// The data is in raw bytes format.
    /// </summary>
    Bytes = 4
}
