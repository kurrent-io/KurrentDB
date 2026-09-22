// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using System.Threading.Tasks;
using Bunit;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Cluster;
using KurrentDB.Components.Projections;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Authorization;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
// The component class name collides with its namespace; alias the type.
using ProjectionsPage = KurrentDB.Components.Projections.Projections;

namespace KurrentDB.Components.Tests;

// The list-page ribbon is unconditional on the working page: it shows regardless of what the grid holds, and
// stays out of the "not enabled" / "go to the leader" states. The detail-page link is user projections only.
public class ProjectionsGafferRibbonTests {
	const string RibbonText = "author, debug, test and deploy projections";
	const string DetailLinkText = "Debug and deploy projections";

	static Task<AuthenticationState> AuthState(string name) =>
		Task.FromResult(new AuthenticationState(
			new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], authenticationType: "Test"))));

	static ProjectionStatistics Projection(string name) =>
		new() { Name = name, Status = "Running", Mode = ProjectionMode.Continuous, Progress = 100 };

	// ProjectionsService is hand-built because "projections disabled" is a null publisher (see
	// ProjectionsService.Available) and the container won't supply null. GossipMonitor needs an IPublisher of
	// its own, hence the stand-in; unstarted, its CurrentState stays null, which is the state that renders
	// the grid rather than the leader notice.
	static BunitContext PageContext(IPublisher projections) =>
		MudBunit.NewContext(services => {
			services.AddSingleton<IPublisher>(projections ?? new ReplyPublisher(_ => { }));
			services.AddSingleton<IAuthorizationProvider>(new PassthroughAuthorizationProvider());
			services.AddScoped(sp => new ProjectionsService(projections, sp.GetRequiredService<IAuthorizationProvider>()));
			services.AddSingleton<GossipMonitor>();
		});

	static IPublisher StatsPublisher(params ProjectionStatistics[] projections) =>
		new ReplyPublisher(msg => {
			if (msg is ProjectionManagementMessage.Command.GetStatistics q)
				q.Envelope.ReplyWith(new ProjectionManagementMessage.Statistics(projections));
		});

	// The detail page reads stats, then the source query, then the state. Every reply is supplied so no read
	// falls through to its 5s timeout.
	static IPublisher DetailPublisher(string name, string query) =>
		new ReplyPublisher(msg => {
			switch (msg) {
				case ProjectionManagementMessage.Command.GetStatistics s:
					s.Envelope.ReplyWith(new ProjectionManagementMessage.Statistics([Projection(name)]));
					break;
				case ProjectionManagementMessage.Command.GetQuery q:
					q.Envelope.ReplyWith(new ProjectionManagementMessage.ProjectionQuery(
						name, query, emitEnabled: false, projectionType: "JS", trackEmittedStreams: false,
						checkpointsEnabled: true, definition: null, outputConfig: null));
					break;
				case ProjectionManagementMessage.Command.GetState st:
					st.Envelope.ReplyWith(new ProjectionManagementMessage.ProjectionState(
						name, partition: "", state: "{}", position: null));
					break;
			}
		});

	[Fact]
	public async Task Ribbon_shows_when_the_grid_has_only_system_projections() {
		await using var ctx = PageContext(StatsPublisher(Projection("$by_category"), Projection("$streams")));

		var cut = ctx.Render<ProjectionsPage>(p => p.AddCascadingValue(AuthState("admin")));

		cut.WaitForAssertion(() => {
			Assert.Contains("$by_category", cut.Markup);
			Assert.Contains(RibbonText, cut.Markup);
		});
	}

	[Fact]
	public async Task Ribbon_still_shows_once_user_projections_exist() {
		await using var ctx = PageContext(StatsPublisher(Projection("order-totals")));

		var cut = ctx.Render<ProjectionsPage>(p => p.AddCascadingValue(AuthState("admin")));

		cut.WaitForAssertion(() => {
			Assert.Contains("order-totals", cut.Markup);
			Assert.Contains(RibbonText, cut.Markup);
		});
	}

	[Fact]
	public async Task Ribbon_is_absent_when_projections_are_disabled() {
		await using var ctx = PageContext(projections: null!);

		var cut = ctx.Render<ProjectionsPage>(p => p.AddCascadingValue(AuthState("admin")));

		cut.WaitForAssertion(() => {
			Assert.Contains("Projections are not enabled on this server", cut.Markup);
			Assert.DoesNotContain(RibbonText, cut.Markup);
		});
	}

	[Fact]
	public async Task Detail_offers_the_link_for_a_user_projection() {
		await using var ctx = PageContext(DetailPublisher("order-totals", "fromStream('orders')"));

		var cut = ctx.Render<ProjectionDetail>(p => p
			.Add(d => d.Name, "order-totals")
			.AddCascadingValue(AuthState("admin")));

		cut.WaitForAssertion(() => {
			Assert.Contains("fromStream", cut.Markup);
			Assert.Contains(DetailLinkText, cut.Markup);
		});
	}

	// System projections ship with the server, so there is nothing to author locally and no link to offer.
	[Fact]
	public async Task Detail_withholds_the_link_for_a_system_projection() {
		await using var ctx = PageContext(DetailPublisher("$by_category", "fromAll()"));

		var cut = ctx.Render<ProjectionDetail>(p => p
			.Add(d => d.Name, "$by_category")
			.AddCascadingValue(AuthState("admin")));

		cut.WaitForAssertion(() => {
			Assert.Contains("fromAll", cut.Markup);          // the Source section did render
			Assert.DoesNotContain(DetailLinkText, cut.Markup);
		});
	}
}
