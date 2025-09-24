// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

public abstract class TestFixtureWithInterpretedProjection {
	protected ProjectionStateHandlerFactory _stateHandlerFactory;
	protected IProjectionStateHandler _stateHandler;
	protected List<string> _logged;
	protected string _projection;
	protected string _state = null;
	protected string _sharedState = null;
	protected IQuerySources _source;
	private TimeSpan CompilationTimeout { get; } = TimeSpan.FromMilliseconds(1000);
	private TimeSpan ExecutionTimeout { get; } = TimeSpan.FromMilliseconds(500);

	[SetUp]
	public void Setup() {
		_state = null;
		_projection = null;
		Given();
		_logged = [];
		_stateHandlerFactory = new(CompilationTimeout, ExecutionTimeout, ProjectionTrackers.NoOp);
		_stateHandler = CreateStateHandler();
		_source = _stateHandler.GetSourceDefinition();

		if (_state != null)
			_stateHandler.Load(_state);
		else
			_stateHandler.Initialize();

		if (_sharedState != null)
			_stateHandler.LoadShared(_sharedState);
		When();
	}

	protected const string ProjectionType = "js";

	protected virtual IProjectionStateHandler CreateStateHandler() {
		return _stateHandlerFactory.Create(
			"projection", ProjectionType, _projection, true, null, logger: (s, _) => {
				if (s.StartsWith("P:"))
					Console.WriteLine(s);
				else
					_logged.Add(s);
			}); // skip prelude debug output
	}

	protected virtual void When() {
	}

	protected abstract void Given();
}
