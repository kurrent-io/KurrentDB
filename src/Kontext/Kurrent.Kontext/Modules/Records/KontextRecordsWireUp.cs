using Kurrent.Kontext.Records.Indexer;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Kontext.Records;

public static class KontextRecordsWireUp {
    extension(IServiceCollection services) {
        public IServiceCollection AddKontextRecords() {
            services.AddHostedService(sp => new KontextRecordsIndexerService(sp));
            return services;
        }
    }
}
