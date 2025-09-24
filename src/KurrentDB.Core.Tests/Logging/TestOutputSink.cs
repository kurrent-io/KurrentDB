using System;
using System.IO;
using NUnit.Framework;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace KurrentDB.Core.Tests.Logging;

public class TestOutputSink(ITextFormatter textFormatter) : ILogEventSink {
	private readonly ITextFormatter _textFormatter = textFormatter ?? throw new ArgumentNullException(nameof(textFormatter));

	public void Emit(LogEvent logEvent) {
		ArgumentNullException.ThrowIfNull(logEvent);
		var renderSpace = new StringWriter();
		_textFormatter.Format(logEvent, renderSpace);
		var message = renderSpace.ToString().Trim();
		TestContext.WriteLine(message);
	}
}
