// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using Kurrent.Surge;

namespace KurrentDB.Surge.Testing.TUnit.OpenTelemetry;

[PublicAPI]
public record OtelServiceMetadata {
    public static readonly OtelServiceMetadata None = new();

    const string ServiceNameKey       = "service.name";
    const string ServiceInstanceIdKey = "service.instance.id";
    const string ServiceVersionKey    = "service.version";
    const string ServiceNamespaceKey  = "service.namespace";
    const string AttributeSeparator   = ",";
    const string ValueSeparator       = "=";
    const string Format               = $"{{0}}{ValueSeparator}{{1}}";

    OtelServiceMetadata() => ServiceName = string.Empty;

    public OtelServiceMetadata(string serviceName) =>
        ServiceName = Ensure.NotNullOrWhiteSpace(serviceName);

    public string  ServiceName       { get; }
    public string? ServiceInstanceId { get; init; }
    public string? ServiceVersion    { get; init; }
    public string? ServiceNamespace  { get; init; }

    public string GetResourceAttributes() {
        var attributes = new List<string>(4) {
            CreateAttribute(ServiceNameKey, ServiceName)
        };

        if (ServiceInstanceId is not null)
            attributes.Add(CreateAttribute(ServiceInstanceIdKey, ServiceInstanceId));

        if (ServiceVersion is not null)
            attributes.Add(CreateAttribute(ServiceVersionKey, ServiceVersion));

        if (ServiceNamespace is not null)
            attributes.Add(CreateAttribute(ServiceNamespaceKey, ServiceNamespace));

        return string.Join(AttributeSeparator, attributes);

        static string CreateAttribute(params object?[] arguments) =>
            FormattableStringFactory.Create(Format, arguments).ToString();
    }

    public void UpdateEnvironmentVariables() {
        Environment.SetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES", GetResourceAttributes());
        Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", ServiceName); // not really necessary, but follows the convention
    }

    public override string ToString() => GetResourceAttributes();

    public static OtelServiceMetadata Parse(string resourceAttributes) {
        var attributes = resourceAttributes
            .Split(AttributeSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Split(ValueSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(x => x.Length > 1 && string.IsNullOrWhiteSpace(x[1]))
            .ToDictionary(x => x[0], x => x[1]);

        return attributes.TryGetValue(ServiceNameKey, out var serviceName)
            ? new(serviceName) {
                ServiceInstanceId = attributes.TryGetValue(ServiceInstanceIdKey, out var instanceId) ? instanceId : null,
                ServiceVersion    = attributes.TryGetValue(ServiceVersionKey, out var version) ? version : null,
                ServiceNamespace  = attributes.TryGetValue(ServiceNamespaceKey, out var ns) ? ns : null
            }
            : None;
    }

    public static implicit operator string(OtelServiceMetadata metadata)           => metadata.GetResourceAttributes();
    public static implicit operator OtelServiceMetadata(string resourceAttributes) => Parse(resourceAttributes);
};
