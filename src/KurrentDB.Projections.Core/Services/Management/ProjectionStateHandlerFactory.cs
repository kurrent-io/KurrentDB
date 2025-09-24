// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services.Interpreted;

namespace KurrentDB.Projections.Core.Services.Management;

public class ProjectionStateHandlerFactory(TimeSpan javascriptCompilationTimeout, TimeSpan javascriptExecutionTimeout, ProjectionTrackers trackers) {
	public IProjectionStateHandler Create(
		string projectionName,
		string factoryType,
		string source,
		bool enableContentTypeValidation,
		int? projectionExecutionTimeout,
		Action<string, object[]> logger = null) {
		var colonPos = factoryType.IndexOf(':');
		string kind;
		string rest = null;
		if (colonPos > 0) {
			kind = factoryType[..colonPos];
			rest = factoryType[(colonPos + 1)..];
		} else {
			kind = factoryType;
		}

		IProjectionStateHandler result;
		var executionTimeout = projectionExecutionTimeout is > 0
			? TimeSpan.FromMilliseconds(projectionExecutionTimeout.Value)
			: javascriptExecutionTimeout;
		switch (kind.ToLowerInvariant()) {
			case "js":
				result = new JintProjectionStateHandler(source, enableContentTypeValidation,
					javascriptCompilationTimeout, executionTimeout,
					new(trackers.GetExecutionTrackerForProjection(projectionName)),
					new(trackers.GetSerializationTrackerForProjection(projectionName)));
				break;
			case "native":
				// Allow loading native projections from previous versions
				rest = rest?.Replace("EventStore", "KurrentDB");

				var type = Type.GetType(rest) ?? AppDomain.CurrentDomain.GetAssemblies()
					.Select(v => v.GetType(rest))
					.FirstOrDefault(v => v != null);

				if (type is null) {
					throw new NotSupportedException($"Could not find type \"{rest}\"");
				}

				var handler = Activator.CreateInstance(type, source, logger);
				result = (IProjectionStateHandler)handler;
				break;
			default:
				throw new NotSupportedException($"'{factoryType}' handler type is not supported");
		}

		return result;
	}
}
