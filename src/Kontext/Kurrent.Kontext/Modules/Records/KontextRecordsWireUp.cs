using FluentValidation;
using Kurrent.Kontext.Infrastructure.Validation;
using Kurrent.Kontext.Records.Data;
using Kurrent.Kontext.Records.Grpc;
using Kurrent.Kontext.Records.Indexer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecordsContracts = Kurrent.Kontext.Contracts.Records;

namespace Kurrent.Kontext.Records;

public static class KontextRecordsWireUp {
    extension(IServiceCollection services) {
        /// <summary>
        /// The records service: the indexer that fills the read model, the store that reads it back, and the
        /// search surface over both.
        /// </summary>
        public IServiceCollection AddKontextRecords() {
            services.AddHostedService(sp => new KontextRecordsIndexerService(sp));

            services.TryAddSingleton<KontextRecordsStore>();
            services.TryAddSingleton<IKontextRecords, KontextRecords>();

            services
                .AddRequestValidation()
                .AddGrpcEdge();

            return services;
        }

        IServiceCollection AddRequestValidation() {
            services.TryAddSingleton<RequestValidationService>();
            services.TryAddSingleton<IValidator<RecordsContracts.SearchRequest>, SearchRequestValidator>();
            services.TryAddSingleton<IValidator<RecordsContracts.QueryRequest>, QueryRequestValidator>();
            return services;
        }

        IServiceCollection AddGrpcEdge() {
            services.AddGrpc();
            services.TryAddSingleton<GrpcRecordsService>();
            return services;
        }
    }

    extension(IApplicationBuilder app) {
        public IApplicationBuilder UseKontextRecords() =>
            app.UseEndpoints(ep => ep.MapGrpcService<GrpcRecordsService>());
    }
}
