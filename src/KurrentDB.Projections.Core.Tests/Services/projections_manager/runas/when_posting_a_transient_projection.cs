// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager.runas;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class Authenticated<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private const string ProjectionName = "test-projection";
	private const string ProjectionBody = "fromAll().when({$any:function(s,e){return s;}});";
	private ClaimsPrincipal _testUserPrincipal;

	protected override void Given() {
		_testUserPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
		[
			new Claim(ClaimTypes.Name, "test-user"),
			new Claim(ClaimTypes.Role, "test-role1"),
			new Claim(ClaimTypes.Role, "test-role2")
		], "ES-Test"));

		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return
			new ProjectionManagementMessage.Command.Post(
				GetInputQueue(), ProjectionMode.Transient, ProjectionName,
				new(_testUserPrincipal), "JS", ProjectionBody, enabled: true,
				checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true, enableRunAs: true);
	}

	[Test, Ignore("ignored")]
	public void anonymous_cannot_retrieve_projection_query() {
		GetInputQueue().Publish(
			new ProjectionManagementMessage.Command.GetQuery(Envelope, ProjectionName, ProjectionManagementMessage.RunAs.Anonymous));
		_queue.Process();

		Assert.IsTrue(HandledMessages.OfType<ProjectionManagementMessage.NotAuthorized>().Any());
	}

	[Test]
	public void projection_owner_can_retrieve_projection_query() {
		GetInputQueue()
			.Publish(new ProjectionManagementMessage.Command.GetQuery(Envelope, ProjectionName, new(_testUserPrincipal)));
		_queue.Process();

		var query = HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().FirstOrDefault();
		Assert.NotNull(query);
		Assert.AreEqual(ProjectionBody, query.Query);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class Anonymous<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private const string ProjectionName = "test-projection";
	private const string ProjectionBody = "fromAll().when({$any:function(s,e){return s;}});";

	protected override void Given() {
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return
			new ProjectionManagementMessage.Command.Post(
				GetInputQueue(), ProjectionMode.Continuous, ProjectionName,
				ProjectionManagementMessage.RunAs.Anonymous, "JS", ProjectionBody, enabled: true,
				checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true, enableRunAs: true);
	}

	[Test]
	public void replies_with_not_authorized() {
		Assert.IsTrue(HandledMessages.OfType<ProjectionManagementMessage.NotAuthorized>().Any());
	}
}
