// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public class CustomIndexException(StatusCode statusCode, string message) : RpcException(new Status(statusCode, message), message);
