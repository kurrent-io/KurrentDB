// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services.Interpreted;

namespace KurrentDB.Projections.Core.Services.Management;



public class ProjectionStateHandlerFactory {
	private readonly TimeSpan _javascriptCompilationTimeout;
	private readonly TimeSpan _javascriptExecutionTimeout;
	private readonly ProjectionExecutionTrackers _executionTrackers;
	private readonly ProjectionTrackers _trackers;

	public ProjectionStateHandlerFactory(
		TimeSpan javascriptCompilationTimeout,
		TimeSpan javascriptExecutionTimeout,
		ProjectionExecutionTrackers executionTrackers,
		ProjectionTrackers trackers) {
		_javascriptCompilationTimeout = javascriptCompilationTimeout;
		_javascriptExecutionTimeout = javascriptExecutionTimeout;
		_executionTrackers = executionTrackers;
		_trackers = trackers;
	}
	public IProjectionStateHandler Create(
		string projectionName,
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
					_javascriptCompilationTimeout, executionTimeout,
					new(_executionTrackers.GetTrackerForProjection(projectionName)),
					new(_trackers.GetSerializationTrackerForProjection(projectionName)));
				break;
			case "native":
				// Allow loading native projections from previous versions
				rest = rest?.Replace("EventStore", "KurrentDB");

				var type = Type.GetType(rest);
				if (type == null) {
					type =
						AppDomain.CurrentDomain.GetAssemblies()
							.Select(v => v.GetType(rest))
							.FirstOrDefault(v => v != null);
				}

				if (type is null) {
					throw new NotSupportedException($"Could not find type \"{rest}\"");
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
