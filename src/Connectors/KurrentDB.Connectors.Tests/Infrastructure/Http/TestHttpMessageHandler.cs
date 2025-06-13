// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Utils;
using static System.Threading.Tasks.TaskCreationOptions;

namespace KurrentDB.Connectors.Tests.Infrastructure.Http;

public class TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);

    public static TestHttpMessageHandler Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) {
        var tcs = TaskCompletionSourceFactory.CreateDefault<HttpResponseMessage>(RunContinuationsAsynchronously);
        return new TestHttpMessageHandler(async (req, ct) => {
            await using var registration = ct.Register(() => tcs.TrySetCanceled());
            var result = await Task.WhenAny(send(req), tcs.Task);
            return await result;
        });
    }
}