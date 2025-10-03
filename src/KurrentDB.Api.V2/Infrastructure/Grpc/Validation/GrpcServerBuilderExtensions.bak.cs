// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// // ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault
//
// using Google.Protobuf;
// using Grpc.AspNetCore.Server;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.DependencyInjection.Extensions;
// using Scrutor;
//
// namespace KurrentDB.Api.Infrastructure.Grpc.Validation;
//
// public static class GrpcServerBuilderExtensions {
//     // public static IServiceCollection AddGrpcRequestValidation(this IServiceCollection services, Action<RequestValidationOptions>? configure = null) {
//     //     var options = new RequestValidationOptions();
//     //
//     //     configure?.Invoke(options);
//     //
//     //     if (options.AutoScanValidators)
//     //         new RequestValidationConfigurator(services).AutoScan();
//     //
//     //     return services
//     //         .AddSingleton(options)
//     //         .AddSingleton<RequestValidation>()
//     //         .AddSingleton<IRequestValidatorProvider, RequestValidatorProvider>();
//     // }
//
//     public static IGrpcServerBuilder WithRequestValidation(this IGrpcServerBuilder builder, Action<RequestValidationOptions>? configure = null) {
//         var options = new RequestValidationOptions();
//         configure?.Invoke(options);
//
//         if (options.AutoScanValidators)
//             new RequestValidationConfigurator(builder.Services).AutoScan();
//
//         builder.Services.TryAddSingleton(options);
//         builder.Services.TryAddSingleton<RequestValidation>();
//         builder.Services.TryAddSingleton<IRequestValidatorProvider, RequestValidatorProvider>();
//
//         return builder;
//     }
//
//     //
//     //
//     // /// <summary>
//     // /// Sets up gRPC request validation
//     // /// </summary>
//     // /// <param name="builder">The gRPC server builder</param>
//     // /// <param name="options">Options for configuring request validation</param>
//     // /// <param name="configure">Configuration delegate for validation setup</param>
//     // /// <returns>The gRPC server builder for chaining</returns>
//     // public static IGrpcServerBuilder WithRequestValidation(this IGrpcServerBuilder builder, RequestValidationOptions? options = null, Action<RequestValidationConfigurator>? configure = null) {
//     //     builder.Services
//     //         .AddSingleton<IRequestValidatorProvider, RequestValidatorProvider>()
//     //         .AddSingleton(options ?? new RequestValidationOptions())
//     //         .AddSingleton<RequestValidation>();
//     //
//     //     configure ??= x => x.AutoScan();
//     //     configure(new RequestValidationConfigurator(builder.Services));
//     //
//     //     return builder;
//     // }
//     //
//     // public static IGrpcServerBuilder WithRequestValidation(this IGrpcServerBuilder builder, Action<RequestValidationOptions> configureOptions, Action<RequestValidationConfigurator>? configure = null) =>
//     //     WithRequestValidation(builder, new RequestValidationOptions().With(configureOptions), configure);
//     //
//     // /// <summary>
//     // /// Sets up gRPC request validation
//     // /// </summary>
//     // /// <param name="builder">The gRPC server builder</param>
//     // /// <param name="configure">Configuration delegate for validation setup</param>
//     // /// <returns>The gRPC server builder for chaining</returns>
//     // public static IGrpcServerBuilder WithRequestValidation(this IGrpcServerBuilder builder, Action<RequestValidationConfigurator> configure) =>
//     //     WithRequestValidation(builder, new RequestValidationOptions(), configure);
// }
//
// /// <summary>
// /// Builder for configuring gRPC request validation
// /// </summary>
// public class RequestValidationConfigurator(IServiceCollection services) {
//     /// <summary>
//     /// Add validators with specific configuration
//     /// </summary>
//     public RequestValidationConfigurator Scan(Action<RequestValidatorsSource> scan) {
//         var configurator = new RequestValidatorsSource(services);
//         scan(configurator);
//         configurator.Scan();
//         return this;
//     }
//
//     /// <summary>
//     /// Scans and adds all validators with Singleton lifetime
//     /// </summary>
//     public RequestValidationConfigurator AutoScan() {
//         new RequestValidatorsSource(services).Scan();
//         return this;
//     }
//
//     public IServiceCollection AddValidatorFor<TRequest>() where TRequest : IMessage {
//         var validatorType = typeof(IRequestValidator<TRequest>);
//
//         return services.Scan(scan => scan
//             .FromApplicationDependencies()
//             .AddClasses(classes => classes.AssignableTo(validatorType), publicOnly: false)
//             .UsingRegistrationStrategy(RegistrationStrategy.Append)
//             .AsSelfWithInterfaces(type => type.IsAssignableTo(RequestValidationConstants.ValidatorInterfaceMarkerType))
//             .WithSingletonLifetime()
//         );
//     }
//
//     /// <summary>
//     /// Register a specific validator type
//     /// </summary>
//     public RequestValidationConfigurator AddValidator<TValidator>()
//         where TValidator : class, IRequestValidator {
//
//         // the type clauses above are not enough it must also implement IRequestValidator<>
//         // otherwise someone could register a class that implements only IRequestValidator
//         // which is not useful
//         var validatorType = typeof(TValidator);
//
//         if (!validatorType.IsRequestValidator())
//             throw new InvalidOperationException($"The type {validatorType.FullName} is not a valid gRPC request validator");
//
//         services.TryAddSingleton(typeof(IRequestValidator), validatorType);
//         services.TryAddSingleton(validatorType, validatorType);
//
//         // services.TryAddEnumerable(
//         //     new ServiceDescriptor(
//         //         serviceType: typeof(IRequestValidator),
//         //         implementationType: typeof(TValidator),
//         //         lifetime: ServiceLifetime.Singleton));
//         //
//         // services.TryAdd(
//         //     new ServiceDescriptor(
//         //         serviceType: typeof(TValidator),
//         //         implementationType: typeof(TValidator),
//         //         lifetime: ServiceLifetime.Singleton));
//
//         return this;
//     }
// }
//
// public class RequestValidatorsSource {
//     internal RequestValidatorsSource(IServiceCollection services) {
//         Services    = services;
//         TypeFilters = [];
//         Lifetime    = ServiceLifetime.Singleton;
//         Namespaces  = [];
//     }
//
//     IServiceCollection Services { get; }
//
//     List<Predicate<Type>> TypeFilters            { get; set; }
//     ServiceLifetime       Lifetime               { get; set; }
//     string[]              Namespaces             { get; set; }
//     Type?                 AssemblyOfTypeFilter   { get; set; }
//
//     /// <summary>
//     /// Add a custom type filter for validator scanning
//     /// </summary>
//     public RequestValidatorsSource Where(Predicate<Type> filter) {
//         TypeFilters.Add(filter);
//         return this;
//     }
//
//     /// <summary>
//     /// Specify assembly containing the given type to scan for validators
//     /// </summary>
//     /// <typeparam name="T">Type whose assembly to scan</typeparam>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidatorsSource FromAssemblyOf<T>() {
//         AssemblyOfTypeFilter = typeof(T);
//         return this;
//     }
//
//     /// <summary>
//     /// Filter validators by namespaces as prefixes
//     /// </summary>
//     /// <param name="namespaces">
//     /// </param>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidatorsSource InNamespaces(params string[] namespaces) {
//         Namespaces = namespaces;
//         return this;
//     }
//
//     /// <summary>
//     /// Set the service lifetime for registered validators
//     /// </summary>
//     /// <param name="lifetime">The service lifetime</param>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidatorsSource WithLifetime(ServiceLifetime lifetime) {
//         Lifetime = lifetime;
//         return this;
//     }
//
//     /// <summary>
//     /// Set singleton lifetime for registered validators
//     /// </summary>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidatorsSource WithSingletonLifetime() =>
//         WithLifetime(ServiceLifetime.Singleton);
//
//     /// <summary>
//     /// Set scoped lifetime for registered validators
//     /// </summary>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidatorsSource WithScopedLifetime() =>
//         WithLifetime(ServiceLifetime.Scoped);
//
//     /// <summary>
//     /// Set transient lifetime for registered validators
//     /// </summary>
//     /// <returns>This builder for chaining</returns>
//     public RequestValidatorsSource WithTransientLifetime() =>
//         WithLifetime(ServiceLifetime.Transient);
//
//     internal void Scan() {
//         // because we need to load the plugin assembly. perhaps I dont even need to do it in this case.
//         var loadedAssemblies = AssemblyLoader.LoadFrom(AppDomain.CurrentDomain.BaseDirectory);
//
//         Services.Scan(scan => {
//             var fromAssemblies = AssemblyOfTypeFilter is not null
//                 ? scan.FromAssembliesOf(AssemblyOfTypeFilter)
//                 : scan.FromAssemblies(loadedAssemblies);
//
//             fromAssemblies
//                 .AddClasses(classes => classes
//                     .InNamespaces(Namespaces)
//                     .AssignableTo(RequestValidationConstants.ValidatorInterfaceMarkerType)
//                     .Where(t => TypeFilters.All(f => f(t))), publicOnly: false)
//                 .UsingRegistrationStrategy(RegistrationStrategy.Append)
//                 .AsSelfWithInterfaces(type => type.IsAssignableTo(RequestValidationConstants.ValidatorInterfaceMarkerType))
//                 .WithLifetime(Lifetime);
//         });
//     }
// }
