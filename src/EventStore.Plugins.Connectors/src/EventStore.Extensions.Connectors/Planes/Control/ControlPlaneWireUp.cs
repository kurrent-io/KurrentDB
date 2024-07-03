using EventStore.Connectors.Control.Coordination;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Connectors.Control;

public static class ControlPlaneWireUp {
    public static void AddConnectorsControlPlane(this IServiceCollection services) {
        // services.AddConnectorsControlPlaneActivation();
        services.AddConnectorsCoordination();
    }
}