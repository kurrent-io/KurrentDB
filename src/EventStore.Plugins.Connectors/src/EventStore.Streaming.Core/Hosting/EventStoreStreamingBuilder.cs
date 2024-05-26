using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Processors.Configuration;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Producers.Configuration;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Schema.Serializers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EventStore.Streaming.Hosting;

[PublicAPI]
public class EventStoreStreamingBuilder {
    public EventStoreStreamingBuilder(IServiceCollection services, IConfiguration configuration) {
        Services      = services;
        Configuration = configuration;
        
        // AddSchemaRegistry(SchemaRegistry.Global);
        // AddStateStore(new InMemoryStateStore());
    }

    protected internal IServiceCollection Services      { get; }
    protected internal IConfiguration     Configuration { get; }
    
    public EventStoreStreamingBuilder AddSchemaRegistry(SchemaRegistry? schemaRegistry = null) {
        Services.TryAddSingleton(schemaRegistry ?? SchemaRegistry.Global);
        Services.TryAddSingleton<ISchemaSerializer>(schemaRegistry ?? SchemaRegistry.Global);
        return this;
    }
    
    public EventStoreStreamingBuilder AddStateStore(IStateStore? stateStore = null) {
        Services.TryAddSingleton(stateStore?? new InMemoryStateStore());
        return this;
    }

    public EventStoreStreamingBuilder AddProcessor<TProcessor, TBuilder, TOptions>(Func<IServiceProvider, IConfiguration, TBuilder, TBuilder> build)
        where TProcessor : class, IProcessor
        where TBuilder : ProcessorBuilder<TBuilder, TOptions>, new()
        where TOptions : ProcessorOptions<TOptions>, new() {
        Services
            .AddSingleton<TProcessor>(
                ctx => {
                    var configuration  = ctx.GetRequiredService<IConfiguration>();
                    var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
                    var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
                    var stateStore     = ctx.GetRequiredService<IStateStore>();

                    var builder = new TBuilder()
                        .LoggerFactory(loggerFactory)
                        .SchemaRegistry(schemaRegistry)
                        .StateStore(stateStore);

                    var processor = build(ctx, configuration, builder).Create();

                    return (TProcessor)processor;
                }
            )
            .AddHostedService<ProcessorWorker<TProcessor>>(
                ctx => {
                    var processor     = ctx.GetRequiredService<TProcessor>();
                    var lifetime      = ctx.GetRequiredService<IHostApplicationLifetime>();
                    var loggerFactory = ctx.GetRequiredService<ILoggerFactory>();

                    return new(processor, lifetime, loggerFactory);
                }
            );

        return this;
    }

    public EventStoreStreamingBuilder AddProducer<TProducer, TBuilder, TOptions>(Func<IServiceProvider, IConfiguration, TBuilder, TBuilder> build)
        where TBuilder : ProducerBuilder<TBuilder, TOptions, TProducer>, new()
        where TOptions : ProducerOptions, new()
        where TProducer : class, IProducer {
        Services.AddSingleton(
            ctx => {
                var configuration  = ctx.GetRequiredService<IConfiguration>();
                var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
                var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();

                var builder = new TBuilder()
                    .LoggerFactory(loggerFactory)
                    .SchemaRegistry(schemaRegistry);

                var producer = build(ctx, configuration, builder).Create();

                return producer;
            }
        );

        return this;
    }

    EventStoreStreamingBuilder AddConsumer<TConsumer, TBuilder, TOptions>(Func<IServiceProvider, IConfiguration, TBuilder, TBuilder> build, string? serviceKey)
        where TBuilder : ConsumerBuilder<TBuilder, TOptions, TConsumer>, new()
        where TOptions : ConsumerOptions, new()
        where TConsumer : class, IConsumer {
        if (string.IsNullOrWhiteSpace(serviceKey)) {
            Services.AddSingleton(
                ctx => {
                    var configuration  = ctx.GetRequiredService<IConfiguration>();
                    var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
                    var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();

                    var builder = new TBuilder()
                        .LoggerFactory(loggerFactory)
                        .SchemaRegistry(schemaRegistry);

                    var consumer = build(ctx, configuration, builder).Create();

                    return consumer;
                }
            );
        }
        else {
            Services.AddKeyedSingleton(
                serviceKey,
                (ctx, key) => {
                    var configuration  = ctx.GetRequiredService<IConfiguration>();
                    var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
                    var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();

                    var builder = new TBuilder()
                        .LoggerFactory(loggerFactory)
                        .SchemaRegistry(schemaRegistry)
                        .ConsumerName($"{key}");

                    var consumer = build(ctx, configuration, builder).Create();

                    return consumer;
                }
            );
        }

        return this;
    }

    public EventStoreStreamingBuilder AddConsumer<TConsumer, TBuilder, TOptions>(
        string serviceKey, Func<IServiceProvider, IConfiguration, TBuilder, TBuilder> build
    )
        where TBuilder : ConsumerBuilder<TBuilder, TOptions, TConsumer>, new()
        where TOptions : ConsumerOptions, new()
        where TConsumer : class, IConsumer {
        AddConsumer<TConsumer, TBuilder, TOptions>(build, serviceKey);
        return this;
    }

    public EventStoreStreamingBuilder AddConsumer<TConsumer, TBuilder, TOptions>(Func<IServiceProvider, IConfiguration, TBuilder, TBuilder> build)
        where TBuilder : ConsumerBuilder<TBuilder, TOptions, TConsumer>, new()
        where TOptions : ConsumerOptions, new()
        where TConsumer : class, IConsumer {
        AddConsumer<TConsumer, TBuilder, TOptions>(build, null);
        return this;
    }
}