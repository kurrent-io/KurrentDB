// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault

using Google.Protobuf;
using Grpc.AspNetCore.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scrutor;

namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

public static class GrpcServerBuilderExtensions {
    public static IGrpcServerBuilder WithRequestValidation(this IGrpcServerBuilder builder, Action<RequestValidationOptions>? configure = null) {
        var options = new RequestValidationOptions();
        configure?.Invoke(options);

        if (options.AutoScanValidators)
            new RequestValidatorsScanner(builder.Services).Scan();

        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddSingleton<RequestValidation>();
        builder.Services.TryAddSingleton<IRequestValidatorProvider, RequestValidatorProvider>();

        return builder;
    }
}

public class RequestValidationConfigurator(IServiceCollection services) {
    public RequestValidationConfigurator AddValidatorFor<TRequest>() where TRequest : IMessage {
        var validatorType = typeof(IRequestValidator<TRequest>);

        services.Scan(scan => scan
            .FromApplicationDependencies()
            .AddClasses(classes => classes.AssignableTo(validatorType), publicOnly: false)
            .UsingRegistrationStrategy(RegistrationStrategy.Append)
            .AsSelfWithInterfaces(type => type.IsAssignableTo(RequestValidationConstants.ValidatorInterfaceMarkerType))
            .WithSingletonLifetime()
        );

        return this;
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

         return this;
     }
}

class RequestValidatorsScanner {
    internal RequestValidatorsScanner(IServiceCollection services) {
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
    public RequestValidatorsScanner Where(Predicate<Type> filter) {
        TypeFilters.Add(filter);
        return this;
    }

    /// <summary>
    /// Specify assembly containing the given type to scan for validators
    /// </summary>
    /// <typeparam name="T">Type whose assembly to scan</typeparam>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsScanner FromAssemblyOf<T>() {
        AssemblyOfTypeFilter = typeof(T);
        return this;
    }

    /// <summary>
    /// Filter validators by namespaces as prefixes
    /// </summary>
    /// <param name="namespaces">
    /// </param>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsScanner InNamespaces(params string[] namespaces) {
        Namespaces = namespaces;
        return this;
    }

    /// <summary>
    /// Set the service lifetime for registered validators
    /// </summary>
    /// <param name="lifetime">The service lifetime</param>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsScanner WithLifetime(ServiceLifetime lifetime) {
        Lifetime = lifetime;
        return this;
    }

    /// <summary>
    /// Set singleton lifetime for registered validators
    /// </summary>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsScanner WithSingletonLifetime() =>
        WithLifetime(ServiceLifetime.Singleton);

    /// <summary>
    /// Set scoped lifetime for registered validators
    /// </summary>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsScanner WithScopedLifetime() =>
        WithLifetime(ServiceLifetime.Scoped);

    /// <summary>
    /// Set transient lifetime for registered validators
    /// </summary>
    /// <returns>This builder for chaining</returns>
    public RequestValidatorsScanner WithTransientLifetime() =>
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
