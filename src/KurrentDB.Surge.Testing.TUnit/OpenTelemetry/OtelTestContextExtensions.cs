namespace KurrentDB.Surge.Testing.TUnit.OpenTelemetry;

public static class OtelTestContextExtensions {
    public static void SetOtelServiceMetadata(this TestContext context, OtelServiceMetadata metadata) {
        context.ObjectBag["OTEL_RESOURCE_ATTRIBUTES"] = metadata.GetResourceAttributes();
        context.ObjectBag["OTEL_SERVICE_NAME"]        = metadata.ServiceName; // not really necessary, but follows the convention
    }

    public static OtelServiceMetadata GetOtelServiceMetadata(this TestContext? context) {
        return context is not null
            && context.ObjectBag.TryGetValue("OTEL_RESOURCE_ATTRIBUTES", out var value)
            && value is string resourceAttributes
            ? OtelServiceMetadata.Parse(resourceAttributes)
            : OtelServiceMetadata.None;
    }

    public static void SetOtelServiceName(this TestContext context, string serviceName) =>
        SetOtelServiceMetadata(context, new(serviceName));
}
