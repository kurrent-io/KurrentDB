using Grpc.AspNetCore.Server;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Protocol.V2.Streams;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KurrentDB.Api.Tests.Infrastructure.Grpc.Validation;

public class RequestValidationTests {
    static IServiceProvider ConfigureValidation(Action<IGrpcServerBuilder> configure) {
        var services = new ServiceCollection()
            .AddLogging(x => x.AddSerilog());

        configure(services.AddGrpc());

        return services.BuildServiceProvider();
    }

    [Test]
    public async ValueTask registers_validator_by_request_type() {
        // Arrange
        var serviceProvider = ConfigureValidation(grpc =>
            grpc.AddRequestValidation(x => x.AddValidatorFor<AppendRequest>()));

        var validatorProvider = serviceProvider.GetRequiredService<IRequestValidatorProvider>();

        // Act
        var validator = validatorProvider.GetValidatorFor<AppendRequest>();

        // Assert
        await Assert.That(validator).IsNotNull();
        await Assert.That(validator).IsTypeOf<AppendRequestValidator>();
    }

    [Test]
    public async ValueTask registers_validator_by_type() {
        // Arrange
        var serviceProvider = ConfigureValidation(grpc =>
            grpc.AddRequestValidation(x => x.AddValidator<AppendRequestValidator>()));

        var validatorProvider = serviceProvider.GetRequiredService<IRequestValidatorProvider>();

        // Act
        var validator = validatorProvider.GetValidatorFor<AppendRequest>();

        // Assert
        await Assert.That(validator).IsNotNull();
        await Assert.That(validator).IsTypeOf<AppendRequestValidator>();
    }

    [Test]
    public async ValueTask validates_request() {
        // Arrange
        var serviceProvider = ConfigureValidation(grpc =>
                grpc.AddRequestValidation(x => x.AddValidator<AppendRequestValidator>()));

        var requestValidation = serviceProvider.GetRequiredService<RequestValidation>();

        // Act
        var validate = () => requestValidation.ValidateRequest(new AppendRequest());

        // Assert
        var result = validate.ShouldNotThrow();

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public void ensures_request_is_valid() {
        // Arrange
        var serviceProvider = ConfigureValidation(grpc =>
            grpc.AddRequestValidation(x => x.AddValidator<AppendRequestValidator>()));

        var requestValidation = serviceProvider.GetRequiredService<RequestValidation>();

        // Act & Assert
        Assert.Throws<InvalidRequestException>(
            () => requestValidation.EnsureRequestIsValid(new AppendRequest()));
    }
}
