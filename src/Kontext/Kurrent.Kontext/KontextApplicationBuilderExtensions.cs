// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Edges.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Kurrent.Kontext;

public static class KontextApplicationBuilderExtensions {
    const string McpBasePath = "/kontext/mcp";

    extension(IApplicationBuilder app) {
        /// <summary>
        /// Maps the MCP edge at <c>/kontext/mcp</c> behind an authenticated-user gate. A
        /// Kontext-owned authorization operation replaces the plain gate when the operations
        /// taxonomy lands; the workspace-era operations are gone with the workspaces.
        /// </summary>
        public IApplicationBuilder UseKontextMcp() {
            app.Use(async (context, next) => {
                if (context.Request.Path.StartsWithSegments(McpBasePath) && context.User.Identity?.IsAuthenticated != true) {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                await next();
            });

            return app.UseEndpoints(endpoints => endpoints.MapMcp(McpBasePath));
        }

        /// <summary>Maps the gRPC memory service.</summary>
        public IApplicationBuilder UseKontextGrpc() =>
            app.UseEndpoints(endpoints => endpoints.MapGrpcService<GrpcMemoryService>());
    }
}
