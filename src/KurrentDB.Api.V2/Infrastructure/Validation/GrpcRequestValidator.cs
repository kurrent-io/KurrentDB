// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using Google.Protobuf;
using Grpc.AspNetCore.Server;
using KurrentDB.Api.Errors;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Api.Infrastructure.Validation;

public class GrpcRequestValidator(IServiceProvider serviceProvider) {
    public ValidationResult ValidateRequest<T>(T request) where T : IMessage =>
	    serviceProvider.GetRequiredService<IValidator<T>>().Validate(request);

    public void EnsureRequestIsValid<T>(T request) where T : IMessage {
        var result = ValidateRequest(request);
        if (!result.IsValid)
            throw ApiErrors.InvalidRequest(result);
    }
}

public static class GrpcRequestValidatorWireUpExtensions {
    public static void AddRequestValidation(this IGrpcServerBuilder builder, Assembly? assembly = null) =>
	    builder.Services
		    .AddValidatorsFromAssembly(assembly ?? Assembly.GetExecutingAssembly(), ServiceLifetime.Singleton)
		    .AddSingleton<GrpcRequestValidator>();
}
