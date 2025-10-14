// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MethodHasAsyncOverload

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Api.Tests.Infrastructure;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Streams.Validators;

[Category("Validation")]
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
        var vex = await Assert
            .That(() => SchemaFormatValidator.Instance.ValidateAndThrow(value))
            .Throws<DetailedValidationException>();

        vex.LogValidationErrors<SchemaFormatValidator>();
    }
}
