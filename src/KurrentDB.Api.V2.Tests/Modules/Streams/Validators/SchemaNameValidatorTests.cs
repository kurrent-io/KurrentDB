// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MethodHasAsyncOverload

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Api.Tests.Infrastructure;
using TUnit.Assertions.AssertConditions;

namespace KurrentDB.Api.Tests.Streams.Validators;

[Category("Validation")]
public class SchemaNameValidatorTests {
    [Test]
    [Arguments("urn:com:kurrentdb:schemas:orders:v2")]
    [Arguments("OrderDetails.V2")]
    [Arguments("event-type")]
    public async ValueTask validates_correctly(string? value) {
        var result = SchemaNameValidator.Instance.Validate(value);
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    public async ValueTask throws_when_value_is_empty(string? value) {
        var vex = await Assert
            .That(() => SchemaNameValidator.Instance.ValidateAndThrow(value))
            .Throws<DetailedValidationException>()
            .WithMessageMatching(StringMatcher.AsWildcard("*must not be empty*"));

        vex.LogValidationErrors<SchemaNameValidator>();
    }

    [Test]
    [Arguments("Invalid/Schema\\Name")]
    [Arguments("Invalid?Schema*Name")]
    [Arguments("Invalid|Schema\"Name")]
    [Arguments("Invalid<Schema>Name")]
    public async ValueTask throws_when_value_as_invalid_format(string? value) {
        var vex = await Assert
            .That(() => SchemaNameValidator.Instance.ValidateAndThrow(value))
            .Throws<DetailedValidationException>()
            .WithMessageMatching(StringMatcher.AsWildcard("*can only contain *"));

        vex.LogValidationErrors<SchemaNameValidator>();
    }
}
