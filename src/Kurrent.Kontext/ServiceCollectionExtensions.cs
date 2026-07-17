// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using Kurrent.Kontext.Edges.Grpc;
using Kurrent.Kontext.Infrastructure.FluentValidation;
using Kurrent.Kontext.Infrastructure.Validation;
using Kurrent.Kontext.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kurrent.Kontext;

public static class KontextServiceCollectionExtensions {
    extension(IServiceCollection services) {
        /// <summary>
        /// Registers the transport-neutral memory service: the explicit request validators, the validation
        /// decorator, and <see cref="IKontextMemory"/> itself. Both edges build on this and it is idempotent,
        /// so registering both edges is safe. The host must supply the <c>VectorStore</c> the core persists to.
        /// </summary>
        public IServiceCollection AddKontext() {
            services.AddCore();
            services.AddGrpcEdge();
            services.AddMcpEdge();
            return services;
        }

        IServiceCollection AddCore() {
            services.TryAddSingleton<KontextMemory>();

            services.TryAddSingleton<IKontextMemory>(sp => new KontextMemoryValidationDecorator(
                sp.GetRequiredService<KontextMemory>(),
                sp.GetRequiredService<RequestValidationService>()));
        
            services.AddRequestValidation();
            
            return services;
        }

        IServiceCollection AddRequestValidation() {
            services.TryAddSingleton<IValidator<Contracts.RetainRequest>, RetainRequestValidator>();
            services.TryAddSingleton<IValidator<Contracts.RetractRequest>, RetractRequestValidator>();
            services.TryAddSingleton<IValidator<Contracts.RecallRequest>, RecallRequestValidator>();
            services.TryAddSingleton<IValidator<Contracts.ReclaimRequest>, ReclaimRequestValidator>();
            services.TryAddSingleton<IValidator<Contracts.RecollectRequest>, RecollectRequestValidator>();
            services.TryAddSingleton<IValidator<Contracts.ReflectRequest>, ReflectRequestValidator>();
            services.TryAddSingleton<RequestValidationService>();
            return services;
        }

        IServiceCollection AddGrpcEdge() {
            services.TryAddSingleton<GrpcMemoryService>();
            return services;
        }

        IMcpServerBuilder AddMcpEdge() {
            // All agent-facing text — server instructions, tool and parameter descriptions, and model schema
            // descriptions — lives in McpInstructions.resx and is applied by WithToolsFromResources.
            services.TryAddSingleton<McpMemoryService>();

            return services
                .AddMcpServer(options => options.ServerInstructions = McpInstructions.Server)
                .WithToolsFromResources<McpMemoryService>();
        }
    }
}