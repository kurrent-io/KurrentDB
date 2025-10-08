// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MethodHasAsyncOverload

using FluentValidation;
using Google.Protobuf;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Api.Tests.Infrastructure;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Streams.Validators;

[Category("Validation")]
public class AppendRequestValidatorTests {
    [Test]
    public async ValueTask throws_when_stream_is_missing() {
        var value = new AppendRequest {
            Records = {
                new AppendRecord {
                    Schema = new SchemaInfo {
                        Name   = "Valid.Name",
                        Format = SchemaFormat.Json
                    },
                    Data = ByteString.Empty
                }
            }
        };

        var vex = await Assert
            .That(() => AppendRequestValidator.Instance.ValidateAndThrow(value))
            .Throws<DetailedValidationException>();

        vex.LogValidationErrors<AppendRequestValidator>();
    }

    [Test]
    [Arguments(-3)]
    [Arguments(-10)]
    public async ValueTask throws_when_expected_revision_is_invalid(long invalidRevision) {
        var value = new AppendRequest {
            Stream = "Valid-Stream",
            ExpectedRevision = invalidRevision,
            Records = {
                new AppendRecord {
                    Schema = new SchemaInfo {
                        Name   = "Valid.Name",
                        Format = SchemaFormat.Json
                    },
                    Data = ByteString.Empty
                }
            }
        };

        var vex = await Assert
            .That(() => AppendRequestValidator.Instance.ValidateAndThrow(value))
            .Throws<DetailedValidationException>();

        vex.LogValidationErrors<AppendRequestValidator>();
    }
}
