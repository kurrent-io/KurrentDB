// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MethodHasAsyncOverload

using FluentValidation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Protocol.V2.Streams;
using ValidationException = FluentValidation.ValidationException;

namespace KurrentDB.Api.Tests.Streams.Validators;

public class SchemaFormatValidatorTests {
    [Test]
    [Arguments(SchemaFormat.Json)]
    [Arguments(SchemaFormat.Protobuf)]
    [Arguments(SchemaFormat.Bytes)]
    [Arguments(SchemaFormat.Avro)]
    public async ValueTask validates_correctly(SchemaFormat value) {
        var result = SchemaFormatValidator.Instance.Validate(value);
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments(SchemaFormat.Unspecified)]
    [Arguments((SchemaFormat)999)]
    public async ValueTask throws_when_invalid(SchemaFormat value) {
        await Assert
            .That(() => SchemaFormatValidator.Instance.ValidateAndThrow(value))
            .Throws<ValidationException>();
    }
}
