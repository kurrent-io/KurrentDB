// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Edges.Grpc;
using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Infrastructure.FluentValidation;
using Kurrent.Kontext.Infrastructure.Validation;
using Kurrent.Kontext.Mcp;
using Kurrent.Kontext.Modules.Memory;
using Kurrent.Kontext.Modules.Records;
using KurrentDB.Core;
using KurrentDB.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kurrent.Kontext;

public static class KontextServiceCollectionExtensions {
    extension(IServiceCollection services) {
        #region ->> Composition Root <<-

        /// <summary>
        /// The whole system in one call: binds <see cref="KontextOptions"/> from
        /// <see cref="KontextOptions.SectionName"/> and chains the logical groups. Hosts wanting a
        /// partial composition call the groups directly.
        /// </summary>
        public IServiceCollection AddKontext(IConfiguration configuration) {
            var options = configuration.GetSection(KontextOptions.SectionName).Get<KontextOptions>() ?? new();

            return services
                .AddKontextOptions(options)
                .AddKontextStorage(options)
                .AddKontextEmbeddings(options.Embeddings)
                .AddKontextRetrieval()
                .AddKontextMemory()
                .AddKontextGrpcEdge()
                .AddKontextMcpEdge()
                .AddKontextIndexing();
        }

        #endregion // Composition Root

        #region ->> Groups <<-

        public IServiceCollection AddKontextOptions(KontextOptions options) {
            services.AddSingleton(options);

            // The dimension is the ONE value shared by the schema (FLOAT[N]) and the embeddings
            // provider; deriving the schema options here keeps a single source of truth.
            services.AddSingleton(new KontextSchemaOptions { Dimension = options.Embeddings.Dimension });

            services.TryAddSingleton<KontextMemoryOptions>();

            return services;
        }

        /// <summary>
        /// The engine needs no file: everything durable lives in lance (tables AND checkpoints),
        /// so each connection opens an in-memory catalog and the lance ATTACH provides all shared
        /// state — pinned by <c>InMemoryEngineProbeTests</c>.
        /// </summary>
        public IServiceCollection AddKontextStorage(KontextOptions options) {
            services.AddSingleton(sp => new KontextConnectionPool(
                "Data Source=:memory:",
                ResolveStoragePath(options, sp)));

            return services;
        }

        public IServiceCollection AddKontextEmbeddings(KontextEmbeddingsOptions options) {
            services.AddKontextEmbeddings(
                options.Provider,
                options.Local,
                options.OpenAI,
                options.Ollama,
                options.GoogleVertexAI,
                options.AmazonBedrock);

            return services;
        }

        /// <summary>The memory service: the store, the domain workflows, and their validation surface.</summary>
        public IServiceCollection AddKontextMemory() {
            services.TryAddSingleton(sp => new KontextDataStore(
                sp.GetRequiredService<KontextConnectionPool>()));

            services.TryAddSingleton<KontextMemory>();

            services.TryAddSingleton<IKontextMemory>(sp => new KontextMemoryValidationDecorator(
                sp.GetRequiredService<KontextMemory>(),
                sp.GetRequiredService<RequestValidationService>()));

            return services.AddRequestValidation();
        }

        public IServiceCollection AddKontextGrpcEdge() {
            // Self-contained: the server host registers gRPC too, but this group must not
            // depend on it — AddGrpc is internally idempotent.
            services.AddGrpc();
            services.TryAddSingleton<GrpcMemoryService>();
            return services;
        }

        public IServiceCollection AddKontextMcpEdge() {
            // All agent-facing text — server instructions, tool and parameter descriptions, and model schema
            // descriptions — lives in McpInstructions.resx and is applied by WithToolsFromResources.
            services.AddHttpContextAccessor();
            services.TryAddSingleton<McpMemoryService>();

            services
                .AddMcpServer(options => options.ServerInstructions = McpInstructions.Server)
                .WithToolsFromResources<McpMemoryService>()
                .WithHttpTransport();

            return services;
        }

        /// <summary>
        /// The write side: the startup gate runs first (hosted services start in registration
        /// order), so the dimension probe and the memories schema stand before either writer moves.
        /// </summary>
        public IServiceCollection AddKontextIndexing() {
            services.AddHostedService<KontextBootstrapService>();
            services.AddKontextMemoryProjector();
            services.AddKontextRecordsIndexer();

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

        #endregion // Groups
    }

    static string ResolveStoragePath(KontextOptions options, IServiceProvider sp) {
        if (!string.IsNullOrWhiteSpace(options.Path))
            return options.Path;

        var nodeOptions = sp.GetRequiredService<ClusterVNodeOptions>();
        var indexPath = nodeOptions.Database.Index
            ?? Path.Combine(nodeOptions.Database.Db, ESConsts.DefaultIndexDirectoryName);

        return Path.Combine(indexPath, "kontext");
    }
}
