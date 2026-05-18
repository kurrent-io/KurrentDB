// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Connectors.Webhook;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Connectors.Planes.Webhook;

public static class WebhookPlaneWireUp {
    public static IServiceCollection AddWebhookPlane(this IServiceCollection services) {
        services.AddSingleton<WebhookSourceRegistry>();

        return services;
    }

    public static void UseWebhookPlane(this IApplicationBuilder application) {
        var registry = application.ApplicationServices.GetRequiredService<WebhookSourceRegistry>();

        application.UseEndpoints(endpoints => {
	        endpoints.MapPost("/webhook/{connectorId}", (HttpContext ctx, string connectorId)
		        => WebhookHandler.Ingest(ctx, connectorId, registry));
        });
    }
}
