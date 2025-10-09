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
public class StreamNameValidatorTests {
    [Test]
    [Arguments("Orders-B8333F7B-32C3-46D4-862D-29823DB6B494")]
    [Arguments("Planets-41")]
    [Arguments("$Cars-Bmw")]
    public async ValueTask validates_correctly(string? value) {
        var result = StreamNameValidator.Instance.Validate(value);
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments("", "*must not be empty*")]
    [Arguments(" ", "*must not be empty*")]
    [Arguments("$$", "*must not be '$$'*")]
    public async Task throws_when_invalid(string? value, string match) {
        var vex = await Assert
            .That(() => StreamNameValidator.Instance.ValidateAndThrow(value))
            .Throws<DetailedValidationException>()
            .WithMessageMatching(StringMatcher.AsWildcard(match));

        vex.LogValidationErrors<StreamNameValidator>();
    }
}
