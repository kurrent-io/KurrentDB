// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CA2254

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;
using Serilog;

namespace KurrentDB.Api.Tests.Infrastructure;

public static class ValidationExceptionLoggingExtensions {
    public static void LogValidationErrors<T>(this DetailedValidationException? vex) where T : IValidator {
        Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, TestContext.Current?.GetDisplayName())
            .ForContext("ValidatorType", typeof(T).FullName)
            .Information(vex!.Message.Replace("\r\n", @"\r\n"));
    }
}
