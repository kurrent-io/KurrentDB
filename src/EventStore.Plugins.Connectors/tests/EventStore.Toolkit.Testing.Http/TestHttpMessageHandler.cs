using static System.Threading.Tasks.TaskCreationOptions;

namespace EventStore.Toolkit.Testing.Http;

public class TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);

    public static TestHttpMessageHandler Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) {
        var tcs = new TaskCompletionSource<HttpResponseMessage>(RunContinuationsAsynchronously);
        return new TestHttpMessageHandler(async (req, ct) => {
            await using var registration = ct.Register(() => tcs.TrySetCanceled());
            var result = await Task.WhenAny(send(req), tcs.Task);
            return await result;
        });
    }
}