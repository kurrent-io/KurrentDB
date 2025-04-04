// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ILogger = Serilog.ILogger;

namespace KurrentDB.TestClient;

internal class CommandsProcessor {
	public IEnumerable<ICmdProcessor> RegisteredProcessors {
		get { return _processors.Values; }
	}

	private readonly Serilog.ILogger _log;
	private readonly IDictionary<string, ICmdProcessor> _processors = new Dictionary<string, ICmdProcessor>();
	private ICmdProcessor _regCommandsProcessor;

	public CommandsProcessor(ILogger log) {
		_log = log;
	}

	public void Register(ICmdProcessor processor, bool usageProcessor = false) {
		var cmd = processor.Keyword.ToUpper();

		if (_processors.ContainsKey(cmd))
			throw new InvalidOperationException(
				string.Format("The processor for command '{0}' is already registered.", cmd));

		_processors[cmd] = processor;

		if (usageProcessor)
			_regCommandsProcessor = processor;
	}

	public bool TryProcess(CommandProcessorContext context, string[] args, out int exitCode) {
		var commandName = args[0].ToUpper();
		var commandArgs = args.Skip(1).ToArray();

		ICmdProcessor commandProcessor;
		if (!_processors.TryGetValue(commandName, out commandProcessor)) {
			_log.Information("Unknown command: {command}.", commandName);
			if (_regCommandsProcessor != null)
				_regCommandsProcessor.Execute(context, new string[0]);
			exitCode = 1;
			return false;
		}

		int exitC = 0;
		var executedEvent = new AutoResetEvent(false);

		ThreadPool.QueueUserWorkItem(_ => {
			try {
				var syntaxOk = commandProcessor.Execute(context, commandArgs);
				if (syntaxOk) {
					exitC = context.ExitCode;
				} else {
					exitC = 1;
					_log.Information("Usage of {command}:", commandName);

					foreach (var s in commandProcessor.Usage.Split("\n")) {
						_log.Information("    {usage}", s);
					}
				}

				executedEvent.Set();
			} catch (Exception exc) {
				_log.Error(exc, "Error while executing command {command}.", commandName);
				exitC = -1;
				executedEvent.Set();
			}
		});

		executedEvent.WaitOne(1000);
		context.WaitForCompletion();

		if (!string.IsNullOrWhiteSpace(context.Reason))
			_log.Error("Error during execution of command: {e}.", context.Reason);
		if (context.Error != null) {
			_log.Error(context.Error, "Error during execution of command");

			var details = new StringBuilder();
			BuildFullException(context.Error, details);
			_log.Error("Details: {details}", details.ToString());
		}

		exitCode = exitC == 0 ? context.ExitCode : exitC;
		return true;
	}

	private static void BuildFullException(Exception ex, StringBuilder details, int level = 0) {
		const int maxLevel = 3;

		if (details == null)
			throw new ArgumentNullException("details");

		if (level > maxLevel)
			return;

		while (ex != null) {
			details.AppendFormat("\n{0}-->{1}", new string(' ', level * 2), ex.Message);

			var aggregated = ex as AggregateException;
			if (aggregated?.InnerExceptions != null) {
				if (level > maxLevel)
					break;

				foreach (var innerException in aggregated.InnerExceptions.Take(2))
					BuildFullException(innerException, details, level + 1);
			} else
				ex = ex.InnerException;

			level += 1;
		}
	}
}
