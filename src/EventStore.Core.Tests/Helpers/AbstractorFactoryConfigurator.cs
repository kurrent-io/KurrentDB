// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.Tests.Helpers;

public class AbstractorFactoryConfigurator<TStreamId> : ILogFormatAbstractorFactory<TStreamId> {
	private readonly ILogFormatAbstractorFactory<TStreamId> _wrapped;
	private readonly Func<LogFormatAbstractorOptions, LogFormatAbstractorOptions> _configure;

	public AbstractorFactoryConfigurator(
		ILogFormatAbstractorFactory<TStreamId> wrapped,
		Func<LogFormatAbstractorOptions, LogFormatAbstractorOptions> configure) {
		_wrapped = wrapped;
		_configure = configure;
	}

	public LogFormatAbstractor<TStreamId> Create(LogFormatAbstractorOptions options) =>
		_wrapped.Create(_configure(options));
}

public static class LogFormatFactoryExtensions {
	public static ILogFormatAbstractorFactory<TStreamId> Configure<TStreamId>(
		this ILogFormatAbstractorFactory<TStreamId> factory,
		Func<LogFormatAbstractorOptions, LogFormatAbstractorOptions> configure) =>

		new AbstractorFactoryConfigurator<TStreamId>(factory, configure);
}
