// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.AspNetCore.Server;
using Grpc.Core;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Protocol.V2.Streams;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KurrentDB.Api.Tests.Infrastructure.Grpc.Validation;

public class RequestValidationTests {
    static IServiceProvider ConfigureValidation(Action<RequestValidationConfigurator> configure) {
        var services = new ServiceCollection()
            .AddLogging(x => x.AddSerilog());

        services
            .AddGrpc()
            .WithRequestValidation(x => x.ExceptionFactory = ApiErrors.InvalidRequest);

        configure(new RequestValidationConfigurator(services));

        return services.BuildServiceProvider();
    }

    [Test]
    public async ValueTask registers_validator_by_request_type() {
        // Arrange
        var serviceProvider = ConfigureValidation(x => x.AddValidatorFor<AppendRequest>());

        var validatorProvider = serviceProvider.GetRequiredService<IRequestValidatorProvider>();

        // Act
        var validator = validatorProvider.GetValidatorFor<AppendRequest>();

        // Assert
        await Assert.That(validator).IsNotNull();
        await Assert.That(validator).IsTypeOf<AppendRequestValidator>();
    }

    [Test]
    public async ValueTask registers_validator_by_type() {
        // Arrange
        var serviceProvider = ConfigureValidation(x => x.AddValidator<AppendRequestValidator>());

        var validatorProvider = serviceProvider.GetRequiredService<IRequestValidatorProvider>();

        // Act
        var validator = validatorProvider.GetValidatorFor<AppendRequest>();

        // Assert
        await Assert.That(validator).IsNotNull();
        await Assert.That(validator).IsTypeOf<AppendRequestValidator>();
    }

    [Test]
    public async ValueTask validates_request() {
        // Arrange
        var serviceProvider = ConfigureValidation(x => x.AddValidator<AppendRequestValidator>());

        var requestValidation = serviceProvider.GetRequiredService<RequestValidation>();

        // Act
        var validate = () => requestValidation.ValidateRequest(new AppendRequest());

        // Assert
        var result = validate.ShouldNotThrow();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public void ensures_request_is_valid() {
        // Arrange
        var serviceProvider = ConfigureValidation(x => x.AddValidator<AppendRequestValidator>());

        var requestValidation = serviceProvider.GetRequiredService<RequestValidation>();

        // Act & Assert
        Assert.Throws<RpcException>(() => requestValidation.EnsureRequestIsValid(new AppendRequest()))
            .StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }
}
