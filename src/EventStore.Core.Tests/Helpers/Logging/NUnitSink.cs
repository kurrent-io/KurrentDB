// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using NUnit.Framework;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace EventStore.Core.Tests.Helpers.Logging;

public class NUnitSink(MessageTemplateTextFormatter formatter) : ILogEventSink {
	private readonly MessageTemplateTextFormatter _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));

	public void Emit(LogEvent logEvent) {
		ArgumentNullException.ThrowIfNull(logEvent);

		var writer = new StringWriter();
		_formatter.Format(logEvent, writer);

		TestContext.Progress.WriteLine(writer.ToString());
	}
}
