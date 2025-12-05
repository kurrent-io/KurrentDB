// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.Runtime;
using KurrentDB.Common.Utils;
using FluentValidation;
using FluentValidation.Results;
using Kurrent.Surge;
using Kurrent.Surge.Connectors.Sinks;

using Kurrent.Surge.Connectors.Sinks.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Sinks.SystemConsole.Themes;

namespace Kurrent.Connectors.Serilog;

public class SerilogSink : ISink {
    SwitchableLogger Logger { get; set; } = null!;

    public string MetricsLabel => "serilog";

    public async ValueTask Open(SinkOpenContext context) {
        var options = context.Configuration.GetRequiredOptions<SerilogSinkOptions>();

        var loggerConfiguration = new LoggerConfiguration()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .Enrich.WithProperty(Constants.SourceContextPropertyName, nameof(SerilogSink))
            .Enrich.WithProperty("OperatingSystem", new {
                Version  = Environment.OSVersion.VersionString,
                Platform = RuntimeInformation.OsPlatform
            }, true)
            .Enrich.WithProperty("EventStoreDB", new {
                VersionInfo.Version,
                VersionInfo.CommitSha,
                VersionInfo.Timestamp
            }, true)
            .Enrich.WithProperty("ConnectorId", context.ConnectorId)
            .Destructure.ByTransforming<SurgeRecord>(x => new {
                RecordId    = x.Id.ToString(),
                StreamId    = x.StreamId.ToString(),
                LogPosition = x.Position.LogPosition.CommitPosition.ToString(),
                IsRedacted  = x.IsRedacted,
                SchemaInfo  = new {
                    Subject     = x.SchemaInfo.SchemaName,
                    SchemaType  = x.SchemaInfo.SchemaDataFormat,
                    ContentType = x.SchemaInfo.ContentType
                },
                Headers      = x.Headers.WithoutSchemaInfo(),
                Data         = options.IncludeRecordData ? Convert.ToBase64String(x.Data.Span) : null,
                Timestamp    = x.Timestamp
            });

        if (options.HasConfiguration) {
            var serilogConfiguration = await options.DecodeConfiguration().ConfigureAwait(false);
            Logger = new SwitchableLogger(loggerConfiguration
                .ReadFrom.Configuration(serilogConfiguration)
                .CreateLogger());
        }
        else {
            Logger = new SwitchableLogger(loggerConfiguration
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss:fff} {Level:u3}] ({ThreadId:000}) {SourceContext} {Message:lj}{NewLine}{Exception}",
                    theme: AnsiConsoleTheme.Literate,
                    applyThemeToRedirectedOutput: true
                )
                .CreateLogger());
        }

        await ValueTask.CompletedTask;
    }

    public ValueTask Write(SinkWriteContext context) {
        Logger
            .ForContext("Record", context.Record, true)
            .Information("{RecordDescriptor}", context.Record.ToString());

		SinkMetrics.TrackWrite(context.ConnectorId, MetricsLabel);

        return ValueTask.CompletedTask;
    }

    public ValueTask Close(SinkCloseContext context) {
        Logger.Dispose();
        return ValueTask.CompletedTask;
    }
}

[PublicAPI]
public record SerilogSinkOptions : SinkOptions {
    public string Configuration     { get; init; } = "";
    public bool   IncludeRecordData { get; init; } = true;

    public bool HasConfiguration => !string.IsNullOrWhiteSpace(Configuration);

    public async ValueTask<IConfiguration> DecodeConfiguration() {
        await using var ms = new MemoryStream(Convert.FromBase64String(Configuration));
        return new ConfigurationBuilder().AddJsonStream(ms).Build();
    }

    // {
    //     "Serilog": {
    //         "Using": [ "Serilog.Sinks.Seq" ],
    //         "WriteTo": [ {
    //             "Name": "Seq",
    //             "Args": {
    //                 "serverUrl": "http://localhost:5341",
    //                 "payloadFormatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
    //             }
    //         } ]
    //     }
    // }
}

[PublicAPI]
public class SerilogSinkValidator : SinkConnectorValidator<SerilogSinkOptions> {
    public SerilogSinkValidator() {
        When(x => x.HasConfiguration,
            () => RuleFor(x => x)
                .Custom(async void (options, ctx) => {
                    try {
                        await options.DecodeConfiguration().ConfigureAwait(false);
                    } catch (Exception) {
                        ctx.AddFailure(new Failures.ConfigurationEncodingFailure());
                    }
                }));
    }

    public static class Failures {
        public class ConfigurationEncodingFailure : ValidationFailure {
            public ConfigurationEncodingFailure() {
                PropertyName = nameof(SerilogSinkOptions.Configuration);
                ErrorCode    = nameof(ConfigurationEncodingFailure);
                ErrorMessage = "Configuration must be a valid base64 encoded string";
            }
        }
    }
}
