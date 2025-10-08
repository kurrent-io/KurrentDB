// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MethodHasAsyncOverload

using FluentValidation;
using KurrentDB.Api.Streams.Validators;
using ValidationException = FluentValidation.ValidationException;

namespace KurrentDB.Api.Tests.Streams.Validators;

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
    [Arguments("Invalid/Schema\\Name")]
    public async ValueTask throws_when_invalid(string? value) {
        await Assert
            .That(() => SchemaNameValidator.Instance.ValidateAndThrow(value))
            .Throws<ValidationException>();
    }
}
