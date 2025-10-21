// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MethodHasAsyncOverload

using FluentValidation;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Api.Tests.Infrastructure;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Streams.Validators;

[Category("Validation")]
public class AppendRecordValidatorTests {
    [Test]
    public async ValueTask validates_correctly() {
        var value = new AppendRecord {
            Schema = new SchemaInfo {
                Name   = "Valid.Name",
                Format = SchemaFormat.Json,
                Id     = "98EACFBF-E6B6-401F-8FE0-EDC0F161B087"
            },
            Data = ByteString.Empty,
            Properties = {
                { "key", Value.ForBool(true) }
            }
        };

        var result = AppendRecordValidator.Instance.Validate(value);
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    public async ValueTask throws_when_any_property_key_is_empty(string invalidKey) {
        var value = new AppendRecord {
            Schema = new SchemaInfo {
                Name   = "Valid.Name",
                Format = SchemaFormat.Json,
                Id     = "98EACFBF-E6B6-401F-8FE0-EDC0F161B087"
            },
            Data = ByteString.Empty,
            Properties = {
                { invalidKey, Value.ForNull() }
            }
        };

        var vex = await Assert
            .That(() => AppendRecordValidator.Instance.ValidateAndThrow(value))
            .Throws<DetailedValidationException>();

        vex.LogValidationErrors<AppendRecordValidator>();
    }
}
