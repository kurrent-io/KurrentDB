// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using Bunit;
using KurrentDB.Components.Cluster;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Authorization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
// The component class name collides with its namespace; alias the type.
using PersistentSubscriptionsPage = KurrentDB.Components.PersistentSubscriptions.PersistentSubscriptions;
using KurrentDB.Components.PersistentSubscriptions;

namespace KurrentDB.Components.Tests;

// Smoke test: with canned stats, the grid renders the subscription groups grouped by stream.
// Locks in the data path + grouping we built so the auth/render-mode spike can't silently break it.
public class PersistentSubscriptionsRenderTests {
	static MonitoringMessage.PersistentSubscriptionInfo Sub(string stream, string group, long total, long parked) =>
		new() {
			EventSource = stream, GroupName = group, Status = "Live",
			TotalItems = total, TotalInFlightMessages = 1, ParkedMessageCount = parked,
			AveragePerSecond = 5, LastKnownEventPosition = "100", LastCheckpointedEventPosition = "100",
			Connections = []
		};

	static Task<AuthenticationState> AuthState(string name) =>
		Task.FromResult(new AuthenticationState(
			new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], authenticationType: "Test"))));

	[Fact]
	public async Task Renders_subscription_groups_grouped_by_stream() {
		var stats = new List<MonitoringMessage.PersistentSubscriptionInfo> {
			Sub("orders", "billing", 10, 0),
			Sub("orders", "shipping", 20, 1),
		};

		var publisher = new ReplyPublisher(msg => {
			switch (msg) {
				case MonitoringMessage.GetAllPersistentSubscriptionStats q:
					q.Envelope.ReplyWith(new MonitoringMessage.GetPersistentSubscriptionStatsCompleted(
						MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.Success,
						stats, requestedOffset: 0, requestedCount: 50, total: stats.Count));
					break;
			}
		});

		await using var ctx = MudBunit.NewContext(services => {
			services.AddSingleton<IPublisher>(publisher);
			// The service now requires an IAuthorizationProvider; allow-all here (render test, not an authz test).
			services.AddSingleton<EventStore.Plugins.Authorization.IAuthorizationProvider>(new PassthroughAuthorizationProvider());
			services.AddScoped<PersistentSubscriptionsService>();
			// The page reads its role and the leader's endpoint from GossipMonitor (a $mem-gossip
			// subscription). It isn't started here, so CurrentState stays null (neither leader nor a known
			// follower) and the page renders the grid rather than the "go to the leader" notice.
			services.AddSingleton<GossipMonitor>();
		});

		var cut = ctx.Render<PersistentSubscriptionsPage>(ps => ps.AddCascadingValue(AuthState("admin")));

		cut.WaitForAssertion(() => {
			Assert.Contains("billing", cut.Markup);
			Assert.Contains("shipping", cut.Markup);
			Assert.Contains("orders", cut.Markup);   // the stream group
		});
	}
}
