using EventStore.Connect.Connectors;
using EventStore.Connect.Schema;
using EventStore.Connectors.Eventuous;
using EventStore.Connectors.Management.Reactors;
using EventStore.Streaming;
using EventStore.Streaming.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static EventStore.Connectors.ConnectorsSystemConventions.Messages;

namespace EventStore.Connectors.Management;

// [PublicAPI]
// public class DemoConnectorsValidationCatalog {
//     public DemoConnectorsValidationCatalog() {
//         // ValidatorTypes = AssemblyScanner.System
//         //     .ScanClasses<IConnectorValidator>()
//         //     .Where(type => type.Name is not nameof(ConnectorsValidation))
//         //     .ToDictionary(type => (ConnectorValidatorTypeName) type.FullName!);
//
//         ValidatorTypes = new Dictionary<ConnectorValidatorTypeName, Type> {
//             {ConnectorValidatorTypeName.From("EventStore.Connect.Connectors.LoggerSinkValidator"), typeof(LoggerSinkValidator)}
//         };
//
//         Validators = [];
//     }
//
//     Dictionary<ConnectorValidatorTypeName, Type> ValidatorTypes { get; }
//
//     Dictionary<Type, IConnectorValidator> Validators { get; }
//
//     public bool TryGet(ConnectorValidatorTypeName validatorTypeName, [MaybeNullWhen(false)] out IConnectorValidator validator) {
//         if (ValidatorTypes.TryGetValue(validatorTypeName, out var validatorType)) {
//             validator = Validators.GetOrAdd(validatorType, type => (IConnectorValidator)Activator.CreateInstance(type)!);
//             return true;
//         }
//
//         validator = null;
//         return false;
//     }
//
//     public bool TryGet(ConnectorInstanceTypeName connectorInstanceTypeName, [MaybeNullWhen(false)] out IConnectorValidator validator) =>
//         TryGet(ConnectorValidatorTypeName.From($"{connectorInstanceTypeName}Validator"), out validator);
// }
//
// public sealed class DemoConnectorsValidation(DemoConnectorsValidationCatalog? catalog = null) : IConnectorValidator {
//     public static DemoConnectorsValidation System { get; } = new();
//
//     DemoConnectorsValidationCatalog Catalog { get; } = catalog ?? new();
//
//     public ValidationResult ValidateConfiguration(IConfiguration configuration) {
//         var connectorTypeName = (ConnectorInstanceTypeName) configuration
//             .GetRequiredOptions<ConnectorOptions>()
//             .InstanceTypeName;
//
//         if (string.IsNullOrWhiteSpace(connectorTypeName))
//             return ConnectorInstanceTypeNameMissing();
//
//         return Catalog.TryGet(connectorTypeName, out var validator)
//             ? validator.ValidateConfiguration(configuration)
//             : ValidatorNotFoundFailure(connectorTypeName);
//
//         static ValidationResult ConnectorInstanceTypeNameMissing() =>
//             new([new ValidationFailure("ConnectorInstanceTypeName", "Failed to extract connector instance type name from configuration")]);
//
//         static ValidationResult ValidatorNotFoundFailure(string connectorTypeName) =>
//             new([new ValidationFailure("ConnectorTypeName", $"Failed to find validator for {connectorTypeName}")]);
//     }
//
//     public ValidationResult ValidateConfiguration(IDictionary<string, string?> configuration) =>
//         ValidateConfiguration(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
//
//     public void EnsureValid(IDictionary<string, string?> configuration) =>
//         EnsureValid(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
//
//     public void EnsureValid(IConfiguration configuration) {
//         var result = ValidateConfiguration(configuration);
//         if (!result.IsValid)
//             throw new FluentValidation.ValidationException(result.Errors);
//     }
// }
//
// public static class RagingAssemblyLocator {
//     static Dictionary<string, Assembly> _assemblies = [];
//
//     public static List<Assembly> Assemblies => _assemblies.Values.OrderBy(x => x.FullName).ToList();
//
//     public static void Init() {
//         _assemblies = new Dictionary<string, Assembly>();
//         AppDomain.CurrentDomain.AssemblyLoad += CurrentDomain_AssemblyLoad;
//         AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
//     }
//
//     static Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args) {
//         _assemblies.TryGetValue(args.Name, out var assembly);
//         return assembly;
//     }
//
//     static void CurrentDomain_AssemblyLoad(object? sender, AssemblyLoadEventArgs args) {
//         var assembly = args.LoadedAssembly;
//         _assemblies[assembly.FullName!] = assembly;
//     }
// }
//
//
// public sealed class RagingPluginLoader : IDisposable {
//     readonly PluginLoadContext _loadContext;
//
//     public RagingPluginLoader(DirectoryInfo pluginDirectory) {
//         _loadContext = new PluginLoadContext(pluginDirectory);
//     }
//
//     public void Dispose() => _loadContext.Unload();
//
//     public IEnumerable<T> Load<T>() where T : class, new() {
//         foreach (var type in LoadTypes<T>())
//             yield return (T)Activator.CreateInstance(type)!;
//     }
//
//     public IEnumerable<Type> LoadTypes<T>() where T : class {
//         foreach (var assembly in _loadContext.Assemblies)
//         foreach (var type in GetTypes(assembly)) {
//             if (type.FullName?.StartsWith("EventStore.Connect.Connectors") ?? false)
//                 Console.WriteLine("Scanned Type: {0}", type.FullName);
//
//             if (type.IsAssignableTo(typeof(T)) && type is { IsAbstract: false, IsInterface: false })
//                 yield return type;
//         }
//     }
//
//     class PluginLoadContext : AssemblyLoadContext {
//         readonly AssemblyDependencyResolver _resolver;
//
//         public PluginLoadContext(DirectoryInfo directory) : base(true) {
//             _resolver = new AssemblyDependencyResolver(directory.FullName);
//             foreach (var library in directory.GetFiles("*.dll"))
//                 LoadFromAssemblyPath(library.FullName);
//         }
//
//         protected override Assembly? Load(AssemblyName assemblyName) {
//             var path = _resolver.ResolveAssemblyToPath(assemblyName);
//             return path is not null ? LoadFromAssemblyPath(path) : null;
//         }
//     }
//
//     static IEnumerable<Type> GetTypes(Assembly assembly, bool throwOnLoadError = false) {
//         try {
//             return assembly.GetTypes();
//         }
//         catch (ReflectionTypeLoadException ex) {
//             if (throwOnLoadError)
//                 throw;
//
//             return ex.Types.Where(type => type is not null).Cast<Type>();
//         }
//     }
// }
//

public static class ManagementPlaneWireUp {
    public static IServiceCollection AddConnectorsManagementPlane(this IServiceCollection services) {
        // services.Configure<KestrelServerOptions>(
        //     options => {
        //         options.ListenAnyIP(10000, o => o.Protocols = HttpProtocols.Http1AndHttp2);
        //
        //         // options.ConfigureEndpointDefaults(x => x.Protocols = HttpProtocols.Http1AndHttp2);
        //     });
        //
        // services.Configure<RouteOptions>(options => options.SetParameterPolicy<RegexInlineRouteConstraint>("regex"));

        // builder.WebHost.ConfigureKestrel(kestrel => {
        //     kestrel.ListenAnyIP(20000, options => options.Protocols = HttpProtocols.Http1);
        //     // kestrel.ListenAnyIP(21000, options => {
        //     //     options.Protocols = HttpProtocols.Http1AndHttp2;
        //     //     options.UseHttps();
        //     // });
        // });

        // services.AddEndpointsApiExplorer();

        services
            .AddGrpc(x => x.EnableDetailedErrors = true)
            .AddJsonTranscoding(o => o.JsonSettings.WriteIndented = true);

        // services
        //     .AddGrpcSwagger()
        //     .AddSwaggerGen(x => {
        //             x.SwaggerDoc(
        //                 "v1",
        //                 new OpenApiInfo {
        //                     Version     = "v1",
        //                     Title       = "Connectors Management API",
        //                     Description = "The API for managing connectors in EventStore"
        //                 }
        //             );
        //
        //             // var filePath = Path.Combine(AppContext.BaseDirectory, "Server.xml");
        //             // x.IncludeXmlComments(filePath);
        //             // x.IncludeGrpcXmlComments(filePath, includeControllerXmlComments: true);
        //         }
        //     );

        services.AddMessageSchemaRegistration();

        services.AddAggregateStore<SystemEventStore>();

        services.AddSingleton<ConnectorDomainServices.ValidateConnectorSettings>(
            ctx => {
                var validation = ctx.GetService<IConnectorValidator>() ?? new SystemConnectorsValidation();
                return settings => validation.ValidateConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()); // it was an extension
            }
        );

        services.AddCommandService<ConnectorApplication, ConnectorEntity>();

        services.AddConnectorsLifecycleReactor();
        services.AddConnectorsCheckpointReactor();
        // services.AddConnectorsStreamSupervisor();

        return services;
    }

    public static void UseConnectorsManagementPlane(this IApplicationBuilder application) {
        // application
        //     .UseSwagger()
        //     .UseSwaggerUI(x => x.SwaggerEndpoint("/swagger/v1/swagger.json", "Connectors Management API v1"));

        // application.UseHttpsRedirection();

        application
            .UseRouting()
            .UseEndpoints(endpoints => endpoints.MapGrpcService<ConnectorsService>());
    }

    public static void UseConnectorsManagementPlane2(this WebApplication application) {
        // application
        //     .UseSwagger()
        //     .UseSwaggerUI(x => {
        //         x.JsonSerializerOptions = JsonSerializerOptions.Default;
        //         x.SwaggerEndpoint("/swagger/v1/swagger.json", "Connectors Management API v1");
        //     });

        // application.UseHttpsRedirection();

        application.MapGrpcService<ConnectorsService>();
    }

    static IServiceCollection AddMessageSchemaRegistration(this IServiceCollection services) =>
        services.AddSchemaRegistryStartupTask(
            "Connectors Management Schema Registration",
            static async (registry, ct) => {
                Type[] messageTypes = [
                    typeof(Contracts.Events.ConnectorCreated),
                    typeof(Contracts.Events.ConnectorActivating),
                    typeof(Contracts.Events.ConnectorRunning),
                    typeof(Contracts.Events.ConnectorDeactivating),
                    typeof(Contracts.Events.ConnectorStopped),
                    typeof(Contracts.Events.ConnectorFailed),
                    typeof(Contracts.Events.ConnectorRenamed),
                    typeof(Contracts.Events.ConnectorReconfigured),
                    typeof(Contracts.Events.ConnectorReset),
                    typeof(Contracts.Events.ConnectorDeleted),
                    typeof(Contracts.Events.ConnectorPositionCommitted)
                ];

                await messageTypes
                    .Select(messageType => {
                        SchemaInfo schemaInfo = new(GetManagementMessageSubject(messageType.Name), SchemaDefinitionType.Json);
                        return registry.RegisterSchema(schemaInfo, messageType, ct).AsTask();
                    })
                    .WhenAll();
            }
        );
}