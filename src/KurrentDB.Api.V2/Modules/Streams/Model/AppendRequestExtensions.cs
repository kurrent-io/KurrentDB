// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Api.Streams.Validators;
using KurrentDB.Core.Data;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams;

public static class AppendRequestExtensions {
    // public static AppendRequest EnsureValid(this AppendRequest request) =>
    //     AppendRequestValidator.Instance.EnsureValid(request);

    public static long ExpectedRevisionOrAny(this AppendRequest request) =>
        request.HasExpectedRevision ? request.ExpectedRevision : ExpectedVersion.Any;
}
