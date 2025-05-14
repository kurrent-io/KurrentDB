using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf.Reflection;
using Google.Rpc;
using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Grpc.JsonTranscoding;
using Microsoft.Extensions.DependencyInjection;
using static System.Reflection.BindingFlags;

namespace KurrentDB.SchemaRegistry.Infrastructure.Grpc;

public static class GrpcJsonTranscodingExtensions {
    static readonly string[] Props = ["UnarySerializerOptions", "ServerStreamingSerializerOptions"];

    static readonly JsonStringEnumConverter CamelCaseJsonStringEnumConverter = new(JsonNamingPolicy.CamelCase);

    static void ConfigureSerializerOptions(JsonSerializerOptions options) {
        options.PropertyNamingPolicy        = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy         = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(CamelCaseJsonStringEnumConverter);
    }

    public static IServiceCollection ConfigureGrpcJsonTranscoding(this IServiceCollection services) =>
        services.PostConfigure<GrpcJsonTranscodingOptions>(options => {
            foreach (var property in Props.Select(name => options.GetType().GetProperty(name, NonPublic | Instance))) {
                if (property?.GetValue(options) is JsonSerializerOptions serializerOptions)
                    ConfigureSerializerOptions(serializerOptions);
            }
        });

    public static IGrpcServerBuilder AddGrpcWithJsonTranscoding(this IServiceCollection services, Action<GrpcServiceOptions>? configure = null) {
        return services
            .ConfigureGrpcJsonTranscoding()
            .AddGrpc(x => {
                x.EnableDetailedErrors = true;
                configure?.Invoke(x);
            })
            .AddJsonTranscoding(options => {
                options.TypeRegistry = TypeRegistry.FromMessages(BadRequest.Descriptor);
                options.TypeRegistry = TypeRegistry.FromMessages(PreconditionFailure.Descriptor);
                options.TypeRegistry = TypeRegistry.FromMessages(ErrorInfo.Descriptor);
                options.TypeRegistry = TypeRegistry.FromMessages(ResourceInfo.Descriptor);
                options.TypeRegistry = TypeRegistry.FromMessages(RetryInfo.Descriptor);
                options.TypeRegistry = TypeRegistry.FromMessages(QuotaFailure.Descriptor);
                options.TypeRegistry = TypeRegistry.FromMessages(DebugInfo.Descriptor);
                options.TypeRegistry = TypeRegistry.FromMessages(Status.Descriptor);
                options.TypeRegistry = TypeRegistry.FromFiles(ErrorDetailsReflection.Descriptor);
            });
    }
}