using System.Text.Json;
using EventStore.Toolkit.Testing.Fixtures;
using Grpc.Core;

namespace EventStore.Extensions.Connectors.Tests.Management;

[UsedImplicitly]
public class ManagementServerFixture : FastFixture {
    public ManagementServerFixture() {
        OnTearDown = async () => await Server.DisposeAsync();
    }

    TestManagementServer? _server;

    public TestManagementServer Server {
        get { return _server ??= new TestManagementServer(OutputHelper); }
    }

    public async Task<RpcException> ExtractRpcException(HttpResponseMessage httpResponse) {
        var content      = await httpResponse.Content.ReadAsStringAsync();
        var jsonDocument = JsonDocument.Parse(content);
        var jsonObject   = jsonDocument.RootElement;

        var code    = jsonObject.GetProperty("code").GetInt32();
        var message = jsonObject.GetProperty("message").GetString();

        return new RpcException(new Status((StatusCode)code, message!), message!);
    }
}