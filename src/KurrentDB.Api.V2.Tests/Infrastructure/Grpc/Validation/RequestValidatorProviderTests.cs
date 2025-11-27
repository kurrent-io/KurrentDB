// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Infrastructure.Grpc.Validation;

public class RequestValidatorProviderTests {
    IRequestValidatorProvider CreateSut(params IRequestValidator[] validators) =>
        new RequestValidatorProvider(validators).As<IRequestValidatorProvider>();

    [Test]
    public async ValueTask returns_registered_validator() {
        // Arrange

        var validator = new AppendRequestValidator();

        var sut = CreateSut(validator);

        // Act
        var actualValidator = sut.GetValidatorFor<AppendRequest>();

        // Assert
        await Assert.That(actualValidator).IsSameReferenceAs(validator);
    }

    [Test]
    public async ValueTask does_not_return_unregistered_validator() {
        // Arrange
        var sut = CreateSut();

        // Act
        var actualValidator = sut.GetValidatorFor<AppendRequest>();

        // Assert
        await Assert.That(actualValidator).IsNull();
    }
}
