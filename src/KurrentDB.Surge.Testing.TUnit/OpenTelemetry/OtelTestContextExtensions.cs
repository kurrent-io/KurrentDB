// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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
