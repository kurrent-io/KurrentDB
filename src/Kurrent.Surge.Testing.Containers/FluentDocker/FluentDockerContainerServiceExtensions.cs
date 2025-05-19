using System.Collections.Generic;
using System.Net;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;
using Kurrent.Surge;

namespace KurrentDB.Surge.Testing.Containers.FluentDocker;

public static class FluentDockerContainerServiceExtensions {
    // IPAddress.Any defaults to IPAddress.Loopback
    public static IPEndPoint GetPublicEndpoint(this IContainerService service, string portAndProtocol) {
        Ensure.NotNullOrWhiteSpace(portAndProtocol);
        var endpoint = service.ToHostExposedEndpoint(portAndProtocol);
        return endpoint.Address.Equals(IPAddress.Any) ? new IPEndPoint(IPAddress.Loopback, endpoint.Port) : endpoint;
    }

    public static IPEndPoint GetPublicEndpoint(this IContainerService service, int port) =>
        service.GetPublicEndpoint($"{port}/tcp");

    public static ContainerBuilder WithPublicEndpointResolver(this ContainerBuilder builder) =>
        builder.UseCustomResolver((endpoints, portAndProtocol, dockerUri) => {
            var endpoint = endpoints.ToHostPort(portAndProtocol, dockerUri);
            return endpoint.Address.Equals(IPAddress.Any) ? new IPEndPoint(IPAddress.Loopback, endpoint.Port) : endpoint;
        });

    public static CommandResponse<IList<string>> ExecuteCommand(this IContainerService service, string command) {
        var config = service.GetConfiguration();
        return service.DockerHost.Execute(config.Id, command, service.Certificates);
    }
}
