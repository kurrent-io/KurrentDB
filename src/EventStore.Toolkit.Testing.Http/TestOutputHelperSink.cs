using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Sinks.XUnit;

namespace EventStore.Toolkit.Testing.Http;

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