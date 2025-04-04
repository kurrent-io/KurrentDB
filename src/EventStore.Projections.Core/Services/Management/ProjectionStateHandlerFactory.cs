// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Projections.Core.Services.Interpreted;

namespace EventStore.Projections.Core.Services.Management;



public class ProjectionStateHandlerFactory {
	private readonly TimeSpan _javascriptCompilationTimeout;
	private readonly TimeSpan _javascriptExecutionTimeout;

	public ProjectionStateHandlerFactory(TimeSpan javascriptCompilationTimeout, TimeSpan javascriptExecutionTimeout) {
		_javascriptCompilationTimeout = javascriptCompilationTimeout;
		_javascriptExecutionTimeout = javascriptExecutionTimeout;
	}
	public IProjectionStateHandler Create(
		string factoryType, string source,
		bool enableContentTypeValidation,
		int? projectionExecutionTimeout,
		Action<string, object[]> logger = null) {
		var colonPos = factoryType.IndexOf(':');
		string kind = null;
		string rest = null;
		if (colonPos > 0) {
			kind = factoryType.Substring(0, colonPos);
			rest = factoryType.Substring(colonPos + 1);
		} else {
			kind = factoryType;
		}

		IProjectionStateHandler result;
		var executionTimeout = projectionExecutionTimeout is > 0
			? TimeSpan.FromMilliseconds(projectionExecutionTimeout.Value)
			: _javascriptExecutionTimeout;
		switch (kind.ToLowerInvariant()) {
			case "js":
				result = new JintProjectionStateHandler(source, enableContentTypeValidation,
					_javascriptCompilationTimeout, executionTimeout);
				break;
			case "native":
				var type = Type.GetType(rest);
				if (type == null) {
					//TODO: explicitly list all the assemblies to look for handlers
					type =
						AppDomain.CurrentDomain.GetAssemblies()
							.Select(v => v.GetType(rest))
							.FirstOrDefault(v => v != null);
				}

				var handler = Activator.CreateInstance(type, source, logger);
				result = (IProjectionStateHandler)handler;
				break;
			default:
				throw new NotSupportedException(string.Format("'{0}' handler type is not supported", factoryType));
		}

		return result;
	}
}
