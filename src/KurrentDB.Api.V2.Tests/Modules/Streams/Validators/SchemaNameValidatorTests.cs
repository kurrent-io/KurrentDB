// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MethodHasAsyncOverload

using System.Text;
using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Api.Tests.Infrastructure;

namespace KurrentDB.Api.Tests.Streams.Validators;

[Category("Validation")]
public class SchemaNameValidatorTests {
    [Test]
    [Arguments("urn:com:kurrentdb:schemas:orders:v2")]
    [Arguments("OrderDetails.V2")]
    [Arguments("event-type")]
    [Arguments("$connectors-kakfa-001")]
    [Arguments("schéma-événement")]
    [Arguments("Ärztlicher-Bericht")]
    [Arguments("患者記録")]
    [Arguments("заказ.v2")]
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
            .WithMessageMatching(StringMatcher.AsRegex(".*must not be empty.*"));

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
            // StringMatcher.AsWildcard recently stopped matching multiline.
            .WithMessageMatching(StringMatcher.AsRegex(".*can only contain.*"));

        vex.LogValidationErrors<SchemaNameValidator>();
    }

    [Test]
    [Arguments("café")]
    [Arguments("Ärztlicher-Bericht")]
    public async ValueTask throws_when_value_is_not_nfc_normalized(string value) {
        // Construct NFD form at runtime so the test input is unambiguous regardless of the source file's encoding.
        var nfd = value.Normalize(NormalizationForm.FormD);

        var vex = await Assert
            .That(() => SchemaNameValidator.Instance.ValidateAndThrow(nfd))
            .Throws<DetailedValidationException>()
            .WithMessageMatching(StringMatcher.AsRegex(".*NFC.*"));

        vex.LogValidationErrors<SchemaNameValidator>();
    }
}
