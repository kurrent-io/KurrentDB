namespace KurrentDB.Api.Infrastructure.Grpc.Validation;

static class RequestValidationConstants {
    public static readonly Type ValidatorOpenGenericType     = typeof(IRequestValidator<>);
    public static readonly Type ValidatorInterfaceMarkerType = typeof(IRequestValidator);
}
