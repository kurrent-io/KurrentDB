// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MethodHasAsyncOverload

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Api.Tests.Infrastructure;

namespace KurrentDB.Api.Tests.Streams.Validators;

[Category("Validation")]
public class SchemaIdValidatorTests {
    [Test]
    [Arguments("98EACFBF-E6B6-401F-8FE0-EDC0F161B087")]
    public async ValueTask validates_correctly(string? value) {
        var result = SchemaIdValidator.Instance.Validate(value);
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("Invalid/Schema\\Id")]
    [Arguments("00000000-0000-0000-0000-00000000000")]
    public async ValueTask throws_when_invalid(string? value) {
        var vex = await Assert
            .That(() => SchemaIdValidator.Instance.ValidateAndThrow(value))
            .Throws<DetailedValidationException>();

        vex.LogValidationErrors<SchemaIdValidator>();
    }
}
