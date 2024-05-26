// ReSharper disable CheckNamespace

using EventStore.Streaming;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.Configuration;
using Polly;

namespace EventStore.IO.Connectors;

sealed class SinkProxy : RecordHandler {
    public SinkProxy(string connectorId, ISink sink, IConfiguration configuration, IServiceProvider serviceProvider) {
        ConnectorId     = connectorId;
        Sink            = sink;
        Configuration   = configuration;
        ServiceProvider = serviceProvider;
    }

    string           ConnectorId     { get; }
    ISink            Sink            { get; }
    IConfiguration   Configuration   { get; }
    IServiceProvider ServiceProvider { get; }

    ResiliencePipeline ResiliencePipeline { get; set; } = ResiliencePipeline.Empty;

    public void Initialize() {
        try {
            var resiliencePipelineBuilder = new ResiliencePipelineBuilder();
            
            var sinkContext = new SinkContext(
                ConnectorId,
                Configuration,
                ServiceProvider,
                resiliencePipelineBuilder
            );

            Sink.Open(sinkContext);

            ResiliencePipeline = resiliencePipelineBuilder.Build();
        }
        catch (Exception ex) {
            throw new($"Failed to open sink: {ConnectorId}", ex);
        }
    }

    public override async Task Process(RecordContext context) {
        var resilienceContext = ResilienceContextPool.Shared.Get(context.CancellationToken);

        try {
            await ResiliencePipeline.ExecuteAsync(
                async (_, state) => {
                    using var scope = state.RecordContext.Logger.BeginPropertyScope([
                        ("ConnectorId", state.Handler.ConnectorId), 
                        ("RecordId", state.RecordContext.Record.Id)
                    ]);

                    await Sink.Write(state.RecordContext);
                },
                resilienceContext,
                (Handler: this, RecordContext: context)
            );
        }
        finally {
            ResilienceContextPool.Shared.Return(resilienceContext);
        }
    }

    public async ValueTask DisposeAsync() {
        try {
            await Sink.Close();
        }
        catch (Exception ex) {
            throw new($"Failed to close sink: {ConnectorId}", ex);
        }
    }
}

