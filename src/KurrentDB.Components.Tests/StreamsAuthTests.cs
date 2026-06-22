// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using AngleSharp.Dom;
using Bunit;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Streams;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Bus;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
// The component class name collides with its namespace (KurrentDB.Components.Streams.Streams),
// so the bare name binds to the namespace; alias the type.
using StreamsPage = KurrentDB.Components.Streams.Streams;

namespace KurrentDB.Components.Tests;

// "Browse $all" is an affordance gated on whether the user may actually read $all — Authorizer.CanAccessAsync
// for Streams.Read on $all, the same operation the service enforces. (It used to assume an $admins role; the
// operation check means a non-admin who is granted $all read also sees it enabled.) These drive the authorizer
// verdict and assert the button enables/disables to match.
public class StreamsAuthTests {
	static BunitContext CreateContext(bool canReadAll) =>
		MudBunit.NewContext(services => {
			// Reads in OnInitializedAsync are intentionally left unanswered: the button's enabled state
			// is decided from the CanAccessAsync verdict before the stream read is awaited, so it renders regardless.
			services.AddSingleton<IPublisher>(new ReplyPublisher(_ => { }));
			// The verdict the affordance keys off — grant/deny Streams.Read($all). (Service-layer enforcement
			// for the actual reads is covered separately by StreamsServiceAuthorizationTests.)
			services.AddSingleton<EventStore.Plugins.Authorization.IAuthorizationProvider>(new RecordingAuthorizationProvider(grant: canReadAll));
			services.AddScoped<StreamsService>();
			// The page injects GossipMonitor (to redirect Add Record to the leader from a follower). It isn't
			// started here, so CurrentState stays null and it has no bearing on the Browse-$all affordance.
			services.AddSingleton<KurrentDB.Components.Cluster.GossipMonitor>();
		});

	static Task<AuthenticationState> AuthState() {
		var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "user")], authenticationType: "Test");
		return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
	}

	static IElement BrowseAllButton(IRenderedComponent<StreamsPage> cut) =>
		cut.FindAll("button").Single(b => b.TextContent.Contains("Browse $all"));

	[Fact]
	public async Task Browse_all_button_is_enabled_when_user_can_read_all() {
		await using var ctx = CreateContext(canReadAll: true);

		var cut = ctx.Render<StreamsPage>(ps => ps.AddCascadingValue(AuthState()));

		cut.WaitForAssertion(() => Assert.False(BrowseAllButton(cut).HasAttribute("disabled")));
	}

	[Fact]
	public async Task Browse_all_button_is_disabled_when_user_cannot_read_all() {
		await using var ctx = CreateContext(canReadAll: false);

		var cut = ctx.Render<StreamsPage>(ps => ps.AddCascadingValue(AuthState()));

		cut.WaitForAssertion(() => Assert.True(BrowseAllButton(cut).HasAttribute("disabled")));
	}
}
