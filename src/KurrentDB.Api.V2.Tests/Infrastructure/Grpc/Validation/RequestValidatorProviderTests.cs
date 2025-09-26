// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.TUnit;

namespace KurrentDB.Api.Tests.Infrastructure.Grpc.Validation;

public class RequestValidatorProviderTests {
    IRequestValidatorProvider CreateSut(params IRequestValidator[] validators) =>
        new RequestValidatorProvider(validators, TestContext.Current.CreateLogger<RequestValidatorProvider>())
            .As<IRequestValidatorProvider>();

    [Test]
    public async ValueTask returns_registered_validator() {
        // Arrange

        var validator = new AppendRequestValidator();

        var sut = CreateSut(validator);

        // Act
        var result = sut.TryGetValidatorFor<AppendRequest>(out var actualValidator);

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(actualValidator).IsEquivalentTo(validator);
        await Assert.That(actualValidator).IsSameReferenceAs(validator);

        result.ShouldBeTrue();
        actualValidator.ShouldBeSameAs(validator);
    }

    [Test]
    public async ValueTask does_not_return_unregistered_validator() {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.TryGetValidatorFor<AppendRequest>(out var actualValidator);

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(actualValidator).IsNull();
    }
}
