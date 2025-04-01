namespace EventStore.Toolkit.Testing.Http;

public class TestHttpClientFactory(TestHttpMessageHandler testHttpMessageHandler) : IHttpClientFactory {
    public HttpClient CreateClient(string name) => new(testHttpMessageHandler);
}