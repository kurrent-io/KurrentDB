// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MethodHasAsyncOverload

using FluentValidation;
using KurrentDB.Api.Streams.Validators;
using ValidationException = FluentValidation.ValidationException;

namespace KurrentDB.Api.Tests.Streams.Validators;

public class RecordIdValidatorTests {
    [Test]
    [Arguments("98EACFBF-E6B6-401F-8FE0-EDC0F161B087")]
    public async ValueTask validates_correctly(string? value) {
        var result = RecordIdValidator.Instance.Validate(value);
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("Invalid/Record\\Id")]
    [Arguments("00000000-0000-0000-0000-00000000000")]
    public async ValueTask throws_when_invalid(string? value) {
        await Assert
            .That(() => RecordIdValidator.Instance.ValidateAndThrow(value))
            .Throws<ValidationException>();
    }
}
