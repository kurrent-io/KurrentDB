// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MethodHasAsyncOverload

using FluentValidation;
using KurrentDB.Api.Streams.Validators;
using ValidationException = FluentValidation.ValidationException;

namespace KurrentDB.Api.Tests.Streams.Validators;

public class StreamNameValidatorTests {
    [Test]
    [Arguments("Orders-B8333F7B-32C3-46D4-862D-29823DB6B494")]
    [Arguments("Planets-41")]
    public async ValueTask validates_correctly(string? value) {
        var result = StreamNameValidator.Instance.Validate(value);
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("Invalid/Stream\\Name")]
    [Arguments("Planets:41")]
    public async Task throws_when_invalid(string? value) {
        await Assert
            .That(() => StreamNameValidator.Instance.ValidateAndThrow(value))
            .Throws<ValidationException>();
    }
}
