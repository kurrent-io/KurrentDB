// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.TestAdapters;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;
using static KurrentDB.Projections.Core.Messages.ProjectionManagementMessage;
using LogV3StreamId = System.UInt32;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

public class SystemProjectionNames : IEnumerable {
	public IEnumerator GetEnumerator() {
		foreach (var projection in typeof(ProjectionNamesBuilder.StandardProjections).GetFields(
				         BindingFlags.Public |
				         BindingFlags.Static |
				         BindingFlags.FlattenHierarchy)
			         .Where(x => x.IsLiteral && !x.IsInitOnly)
			         .Select(x => x.GetRawConstantValue())) {
			yield return new[] { typeof(LogFormat.V2), typeof(string), projection };
			yield return new[] { typeof(LogFormat.V3), typeof(LogV3StreamId), projection };
		}
	}
}

[TestFixture, TestFixtureSource(typeof(SystemProjectionNames))]
public class when_deleting_a_system_projection<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private readonly string _systemProjectionName;

	public when_deleting_a_system_projection(string projectionName) {
		_systemProjectionName = projectionName;
	}

	protected override bool GivenInitializeSystemProjections() => true;

	protected override void Given() {
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return new Command.Disable(_bus, _systemProjectionName, RunAs.System);
		yield return new Command.Delete(_bus, _systemProjectionName, RunAs.System, false, false, false);
	}

	[Test, Category("v8")]
	public void a_projection_deleted_event_is_not_written() {
		Assert.IsFalse(
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Any(x =>
				x.Events[0].EventType == ProjectionEventTypes.ProjectionDeleted &&
				Helper.UTF8NoBom.GetString(x.Events[0].Data) == _systemProjectionName));
	}
}
