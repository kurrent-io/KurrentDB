// ReSharper disable CheckNamespace

using Microsoft.Extensions.Configuration;
using Polly;

namespace EventStore.IO.Connectors;

public record SinkContext(
    string ConnectorId,
    IConfiguration Configuration,
    IServiceProvider ServiceProvider,
    ResiliencePipelineBuilder ResiliencePipelineBuilder
);