// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.CodeAnalysis;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services.Interpreted;

namespace KurrentDB.Projections.Core.Services.Management;

public class ProjectionStateHandlerFactory {
	private readonly TimeSpan _javascriptCompilationTimeout;
	private readonly TimeSpan _javascriptExecutionTimeout;
	private readonly ProjectionTrackers _trackers;

	public ProjectionStateHandlerFactory(
		TimeSpan javascriptCompilationTimeout,
		TimeSpan javascriptExecutionTimeout,
		ProjectionTrackers trackers) {
		_javascriptCompilationTimeout = javascriptCompilationTimeout;
		_javascriptExecutionTimeout = javascriptExecutionTimeout;
		_trackers = trackers;
	}

	public IProjectionStateHandler Create(
		string projectionName,
		string factoryType, string source,
		bool enableContentTypeValidation,
		int? projectionExecutionTimeout,
		Func<string, string, Action<string, object[]>, IProjectionStateHandler> factory,
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
					new(_trackers.GetExecutionTrackerForProjection(projectionName)),
					new(_trackers.GetSerializationTrackerForProjection(projectionName)));
				break;
			case "native":
				// Allow loading native projections from previous versions
				rest = rest?.Replace("EventStore", "KurrentDB");

				result = factory.Invoke(rest, source, logger)
				         ?? TryLoadProjection(rest, source, logger)
				         ?? throw new NotSupportedException($"Could not find type \"{rest}\"");

				break;
			default:
				throw new NotSupportedException($"'{factoryType}' handler type is not supported");
		}

		return result;

		[UnconditionalSuppressMessage("Trimming", "IL2057",
			Justification = "Dynamic projection loading is for tests and backward compat only.")]
		static IProjectionStateHandler TryLoadProjection(string typeName, string source, Action<string, object[]> logger) {
			var projectionType = Type.GetType(typeName);
			return typeof(IProjectionStateHandler).IsAssignableFrom(projectionType)
				? Activator.CreateInstance(projectionType, source, logger) as IProjectionStateHandler
				: null;
		}
	}
}
