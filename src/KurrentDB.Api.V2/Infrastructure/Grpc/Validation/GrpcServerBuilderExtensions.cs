// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault

using System.Reflection;
using Google.Protobuf;
using Grpc.AspNetCore.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scrutor;

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

public static class GrpcServerBuilderExtensions {
    /// <summary>
    /// Sets up gRPC request validation
    /// </summary>
    /// <param name="builder">The gRPC server builder</param>
    /// <param name="options">Options for configuring request validation</param>
    /// <param name="configure">Configuration delegate for validation setup</param>
    /// <returns>The gRPC server builder for chaining</returns>
    public static IGrpcServerBuilder AddRequestValidation(this IGrpcServerBuilder builder, RequestValidationOptions? options = null, Action<RequestValidationConfigurator>? configure = null) {
        builder.Services
            .AddSingleton<IRequestValidatorProvider, RequestValidatorProvider>()
            .AddSingleton(options ?? new RequestValidationOptions())
            .AddSingleton<RequestValidation>();

        configure ??= x => x.AutoScan();
        configure(new RequestValidationConfigurator(builder.Services));

        return builder;
    }

    /// <summary>
    /// Sets up gRPC request validation
    /// </summary>
    /// <param name="builder">The gRPC server builder</param>
    /// <param name="configure">Configuration delegate for validation setup</param>
    /// <returns>The gRPC server builder for chaining</returns>
    public static IGrpcServerBuilder AddRequestValidation(this IGrpcServerBuilder builder, Action<RequestValidationConfigurator> configure) =>
        AddRequestValidation(builder, null, configure);
}

/// <summary>
/// Builder for configuring gRPC request validation
/// </summary>
public class RequestValidationConfigurator(IServiceCollection services) {
    /// <summary>
    /// Add validators with specific configuration
    /// </summary>
    public RequestValidationConfigurator Scan(Action<RequestValidatorsSource> scan) {
        var configurator = new RequestValidatorsSource(services);
        scan(configurator);
        configurator.Scan();
        return this;
    }

    /// <summary>
    /// Scans and adds all validators with Singleton lifetime
    /// </summary>
    public RequestValidationConfigurator AutoScan() {
        new RequestValidatorsSource(services).Scan();
        return this;
    }

    public IServiceCollection AddValidatorFor<TRequest>() where TRequest : IMessage {
        var validatorType = typeof(IRequestValidator<TRequest>);

        return services.Scan(scan => scan
            .FromApplicationDependencies()
            .AddClasses(classes => classes.AssignableTo(validatorType), publicOnly: false)
            .UsingRegistrationStrategy(RegistrationStrategy.Append)
            .AsSelfWithInterfaces(type => type.IsAssignableTo(RequestValidationConstants.ValidatorInterfaceMarkerType))
            .WithSingletonLifetime()
        );
    }

    /// <summary>
    /// Register a specific validator type
    /// </summary>
    public RequestValidationConfigurator AddValidator<TValidator>()
        where TValidator : class, IRequestValidator {

        // the type clauses above are not enough it must also implement IRequestValidator<>
        // otherwise someone could register a class that implements only IRequestValidator
        // which is not useful
        var validatorType = typeof(TValidator);

        if (!validatorType.IsRequestValidator())
            throw new InvalidOperationException($"The type {validatorType.FullName} is not a valid gRPC request validator");

        services.TryAddSingleton(typeof(IRequestValidator), validatorType);
        services.TryAddSingleton(validatorType, validatorType);

        // services.TryAddEnumerable(
        //     new ServiceDescriptor(
        //         serviceType: typeof(IRequestValidator),
        //         implementationType: typeof(TValidator),
        //         lifetime: ServiceLifetime.Singleton));
        //
        // services.TryAdd(
        //     new ServiceDescriptor(
        //         serviceType: typeof(TValidator),
        //         implementationType: typeof(TValidator),
        //         lifetime: ServiceLifetime.Singleton));

        return this;
    }
}

public class RequestValidatorsSource {
    internal RequestValidatorsSource(IServiceCollection services) {
        Services    = services;
        TypeFilters = [];
        Lifetime    = ServiceLifetime.Singleton;
        Namespaces  = [];
    }

    IServiceCollection Services { get; }

    List<Predicate<Type>> TypeFilters            { get; set; }
    ServiceLifetime       Lifetime               { get; set; }
    string[]              Namespaces             { get; set; }
    Type?                 AssemblyOfTypeFilter   { get; set; }

    /// <summary>
    /// Add a custom type filter for validator scanning
    /// </summary>
    public RequestValidatorsSource Where(Predicate<Type> filter) {
        TypeFilters.Add(filter);
        return this;
    }

    /// <summary>
    /// Specify assembly containing the given type to scan for validators
    /// </summary>
    /// <typeparam name="T">Type whose assembly to scan</typeparam>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsSource FromAssemblyOf<T>() {
        AssemblyOfTypeFilter = typeof(T);
        return this;
    }

    /// <summary>
    /// Filter validators by namespaces as prefixes
    /// </summary>
    /// <param name="namespaces">
    /// </param>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsSource InNamespaces(params string[] namespaces) {
        Namespaces = namespaces;
        return this;
    }

    /// <summary>
    /// Set the service lifetime for registered validators
    /// </summary>
    /// <param name="lifetime">The service lifetime</param>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsSource WithLifetime(ServiceLifetime lifetime) {
        Lifetime = lifetime;
        return this;
    }

    /// <summary>
    /// Set singleton lifetime for registered validators
    /// </summary>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsSource WithSingletonLifetime() =>
        WithLifetime(ServiceLifetime.Singleton);

    /// <summary>
    /// Set scoped lifetime for registered validators
    /// </summary>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsSource WithScopedLifetime() =>
        WithLifetime(ServiceLifetime.Scoped);

    /// <summary>
    /// Set transient lifetime for registered validators
    /// </summary>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsSource WithTransientLifetime() =>
        WithLifetime(ServiceLifetime.Transient);

    internal void Scan() {
        // because we need to load the plugin assembly. perhaps I dont even need to do it in this case.
        var loadedAssemblies = AssemblyLoader.LoadFrom(AppDomain.CurrentDomain.BaseDirectory);

        Services.Scan(scan => {
            var fromAssemblies = AssemblyOfTypeFilter is not null
                ? scan.FromAssembliesOf(AssemblyOfTypeFilter)
                : scan.FromAssemblies(loadedAssemblies);

            fromAssemblies
                .AddClasses(classes => classes
                    .InNamespaces(Namespaces)
                    .AssignableTo(RequestValidationConstants.ValidatorInterfaceMarkerType)
                    .Where(t => TypeFilters.All(f => f(t))), publicOnly: false)
                .UsingRegistrationStrategy(RegistrationStrategy.Append)
                .AsSelfWithInterfaces(type => type.IsAssignableTo(RequestValidationConstants.ValidatorInterfaceMarkerType))
                .WithLifetime(Lifetime);
        });
    }
}

// type.IsGenericType && type.GetGenericTypeDefinition() == RequestValidationConstants.ValidatorOpenGenericType)
// var scanner = AssemblyScanner.UsingDefaultAssemblies(AssemblyFileNameFilter, includeInternalTypes: true);
//
// var types = scanner.Scan().Where(t => TypeFilters.All(f => f(t)));
//
//
// foreach (var validatorType in types) {
//
//     // *** Find the IValidator<> interface implemented by this type
//     // This is needed because a validator might implement multiple IValidator<> interfaces
//     // and we need to register it for each one
//     // e.g. class MyValidator : IValidator<Foo>, IValidator<Bar>
//     //
//     // it looks like I dont need to do this cause we already filtered
//     // by gRPC validators using the IGrpcRequestValidator interface
//     // in that case we can probably register the validatorType as
//     // IGrpcRequestValidator and IValidator<> and as self too
//     var interfaceType = validatorType
//         .GetInterfaces()
//         .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>));
//
//     if (interfaceType is null)
//         continue;
//     // ***
//
//     //Register as interface
//     Services.TryAddEnumerable(
//         new ServiceDescriptor(
//             serviceType: interfaceType,
//             implementationType: validatorType,
//             lifetime: Lifetime));
//
//     //Register as self
//     Services.TryAdd(
//         new ServiceDescriptor(
//             serviceType: validatorType,
//             implementationType: validatorType,
//             lifetime: Lifetime));
// }

// /// <summary>
// /// Builder for configuring validator scanning options
// /// </summary>
// public class RequestValidationScan {
//     static readonly Predicate<Type> IsRequestValidator =
//         static t => typeof(IRequestValidator).IsAssignableFrom(t) && t.IsInstantiableClass();
//
//     internal RequestValidationScan(IServiceCollection services) {
//         Services      = services;
//         TypeFilters   = [IsRequestValidator];
//         Lifetime      = ServiceLifetime.Singleton;
//     }
//
//     IServiceCollection Services { get; }
//
//     List<Predicate<Type>> TypeFilters            { get; set; }
//     ServiceLifetime       Lifetime               { get; set; }
//     Predicate<string>?    AssemblyFileNameFilter { get; set; }
//
//     /// <summary>
//     /// Add a custom type filter for validator scanning
//     /// </summary>
//     public RequestValidationScan Where(Predicate<Type> filter) {
//         TypeFilters.Add(filter);
//         return this;
//     }
//
//     /// <summary>
//     /// Specify assembly containing the given type to scan for validators
//     /// </summary>
//     /// <param name="type">Type whose assembly to scan</param>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidationScan FromAssemblyContaining(Type type) =>
//         Where(t => t.Assembly == type.Assembly);
//
//     /// <summary>
//     /// Specify assembly containing the given type to scan for validators
//     /// </summary>
//     /// <typeparam name="T">Type whose assembly to scan</typeparam>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidationScan FromAssemblyContaining<T>() =>
//         FromAssemblyContaining(typeof(T));
//
//     /// <summary>
//     /// Filter validators by namespace prefix
//     /// </summary>
//     /// <param name="namespacePrefix">The namespace prefix to filter by</param>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidationScan InNamespace(string namespacePrefix) {
//         ArgumentException.ThrowIfNullOrWhiteSpace(namespacePrefix);
//         return Where(t => t.IsInNamespace(namespacePrefix));
//     }
//
//     /// <summary>
//     /// Filter validators by specific validator interface type
//     /// </summary>
//     /// <typeparam name="T">The validator interface type to filter by</typeparam>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidationScan OfType<T>() where T : IRequestValidator =>
//         Where(t => t.IsAssignableFrom(typeof(T)));
//
//     /// <summary>
//     /// Set the service lifetime for registered validators
//     /// </summary>
//     /// <param name="lifetime">The service lifetime</param>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidationScan WithLifetime(ServiceLifetime lifetime) {
//         Lifetime = lifetime;
//         return this;
//     }
//
//     /// <summary>
//     /// Set singleton lifetime for registered validators
//     /// </summary>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidationScan WithSingletonLifetime() =>
//         WithLifetime(ServiceLifetime.Singleton);
//
//     /// <summary>
//     /// Set scoped lifetime for registered validators
//     /// </summary>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidationScan WithScopedLifetime() =>
//         WithLifetime(ServiceLifetime.Scoped);
//
//     /// <summary>
//     /// Set transient lifetime for registered validators
//     /// </summary>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidationScan WithTransientLifetime() =>
//         WithLifetime(ServiceLifetime.Transient);
//
//     /// <summary>
//     /// Executes the validator scanning with the configured options and returns the parent builder for chaining
//     /// </summary>
//     internal void Scan() {
//         var scanner = AssemblyScanner.UsingDefaultAssemblies(AssemblyFileNameFilter, includeInternalTypes: true);
//
//         var types = scanner.Scan().Where(t => TypeFilters.All(f => f(t)));
//
//         foreach (var validatorType in types) {
//
//             // *** Find the IValidator<> interface implemented by this type
//             // This is needed because a validator might implement multiple IValidator<> interfaces
//             // and we need to register it for each one
//             // e.g. class MyValidator : IValidator<Foo>, IValidator<Bar>
//             //
//             // it looks like I dont need to do this cause we already filtered
//             // by gRPC validators using the IGrpcRequestValidator interface
//             // in that case we can probably register the validatorType as
//             // IGrpcRequestValidator and IValidator<> and as self too
//             var interfaceType = validatorType
//                 .GetInterfaces()
//                 .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>));
//
//             if (interfaceType is null)
//                 continue;
//             // ***
//
//             //Register as interface
//             Services.TryAddEnumerable(
//                 new ServiceDescriptor(
//                     serviceType: interfaceType,
//                     implementationType: validatorType,
//                     lifetime: Lifetime));
//
//             //Register as self
//             Services.TryAdd(
//                 new ServiceDescriptor(
//                     serviceType: validatorType,
//                     implementationType: validatorType,
//                     lifetime: Lifetime));
//         }
//     }
// }
//
// static class FluentValidationExtensions {
//     static readonly Type RequestValidatorOpenGenericType = typeof(IRequestValidator<>);
//
//     public static IServiceCollection AddRequestValidator(this IServiceCollection services, Type validatorType, ServiceLifetime lifetime = ServiceLifetime.Singleton) {
//         // if (!validatorType.IsGenericInstanceOf(RequestValidatorOpenGenericType) && !validatorType.IsInstantiableClass())
//         //     throw new ArgumentException($"Type {validatorType.FullName} is not a gRPC request validator");
//
//         var interfaces = validatorType.GetInterfaces().Where(i => i.IsGenericType).ToArray();
//
//         // Register as interface
//         services.TryAddEnumerable(
//             new ServiceDescriptor(
//                 serviceType: interfaces.First(i => i.GetGenericTypeDefinition() == RequestValidationConstants.ValidatorOpenGenericType),
//                 implementationType: validatorType,
//                 lifetime: lifetime));
//
//         // Register as self
//         services.TryAdd(
//             new ServiceDescriptor(
//                 serviceType: validatorType,
//                 implementationType: validatorType,
//                 lifetime: lifetime));
//
//         return services;
//     }
//
//     public static IServiceCollection AddRequestValidator<T>(this IServiceCollection services, T validator, ServiceLifetime lifetime = ServiceLifetime.Singleton) where T : class {
//         var validatorType = typeof(T);
//
//         if (!validatorType.IsGenericInstanceOf(RequestValidatorOpenGenericType) && !validatorType.IsInstantiableClass())
//             throw new ArgumentException($"Type {validatorType.FullName} is not a gRPC request validator");
//
//         var interfaces = validatorType.GetInterfaces().Where(i => i.IsGenericType).ToArray();
//
//         // Register as fluent interface
//         services.TryAddEnumerable(
//             new ServiceDescriptor(
//                 serviceType: interfaces.First(i => i.GetGenericTypeDefinition() == FluentValidatorOpenGenericType),
//                 implementationType: validatorType,
//                 lifetime: lifetime));
//
//         // Register as request interface
//         services.TryAddEnumerable(
//             new ServiceDescriptor(
//                 serviceType: interfaces.First(i => i.GetGenericTypeDefinition() == RequestValidatorOpenGenericType),
//                 implementationType: validatorType,
//                 lifetime: lifetime));
//
//         // Register as self
//         switch (lifetime) {
//             case ServiceLifetime.Singleton:
//                 services.TryAdd(validator);
//                 services.TryAddSingleton(validator);
//                 break;
//
//             case ServiceLifetime.Scoped:
//                 services.TryAddScoped(_ => validator);
//                 break;
//
//             case ServiceLifetime.Transient:
//                 services.TryAddTransient(_ => validator);
//                 break;
//         }
//
//         return services;
//     }
//
//     public static IServiceCollection AddRequestValidator<T>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Singleton) where T : class =>
//         services.AddRequestValidator(typeof(T), lifetime);
// }
