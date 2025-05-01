// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Sinks.XUnit;

namespace KurrentDB.Connectors.Tests.Infrastructure.Http;

public class TestOutputHelperSink : ILogEventSink {
    TestOutputSink? _inner;

    public void SwitchTo(ITestOutputHelper outputHelper) {
        var templateTextFormatter = new MessageTemplateTextFormatter("[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        _inner = new TestOutputSink(outputHelper, templateTextFormatter);
    }

    public void Emit(LogEvent logEvent) {
        try {
            _inner?.Emit(logEvent);
        }
        catch (InvalidOperationException) {
            // Thrown when shutting down and there's no active test. Ignore as logs will make it to Seq.
        }
    }
}