using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Connectors.Management;

public static class ConnectorWireUp {
	public static void AddConnectorsManagement(this IServiceCollection services) {
		services.AddFunctionalService<ConnectorApplication, ConnectorEntity>();

		services.AddSingleton<ConnectorDomainServices.ValidateConnectorSettings>(
			(type, settings, ct) => Task.FromResult((new Dictionary<string, string>(), true))
		);
	}
    
    public static void UseConnectorsManagement(this IApplicationBuilder app) {
        app.UseEndpoints(x => x.MapGrpcService<ConnectorService>());
    }
}