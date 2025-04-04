// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Services;
using EventStore.Core.Services.UserManagement;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Authorization.AuthorizationPolicies;

public class LegacyPolicySelectorFactory : IPolicySelectorFactory {
	private readonly bool _allowAnonymousEndpointAccess;
	private readonly bool _allowAnonymousStreamAccess;
	private readonly bool _overrideAnonymousGossipEndpointAccess;
	public const string LegacyPolicySelectorName = "acl";
	public string CommandLineName => LegacyPolicySelectorName;

	public LegacyPolicySelectorFactory(
		bool allowAnonymousEndpointAccess,
		bool allowAnonymousStreamAccess,
		bool overrideAnonymousGossipEndpointAccess) {
		_allowAnonymousEndpointAccess = allowAnonymousEndpointAccess;
		_allowAnonymousStreamAccess = allowAnonymousStreamAccess;
		_overrideAnonymousGossipEndpointAccess = overrideAnonymousGossipEndpointAccess;
	}

	private static readonly Claim[] Admins =
		{new Claim(ClaimTypes.Role, SystemRoles.Admins), new Claim(ClaimTypes.Name, SystemUsers.Admin)};

	private static readonly Claim[] OperationsOrAdmins = {
		new Claim(ClaimTypes.Role, SystemRoles.Admins), new Claim(ClaimTypes.Name, SystemUsers.Admin),
		new Claim(ClaimTypes.Role, SystemRoles.Operations), new Claim(ClaimTypes.Name, SystemUsers.Operations)
	};

	public Task<bool> Enable() => Task.FromResult(true);

	public Task Disable() => Task.CompletedTask;

	public IPolicySelector Create(IPublisher publisher) {
		var policy = new Policy(LegacyPolicySelectorName, 1, DateTimeOffset.MinValue);
		var legacyStreamAssertion = new LegacyStreamPermissionAssertion(publisher);

		// The Node.Ping is set to allow anonymous as it does not disclose any secure information.
		policy.AllowAnonymous(Operations.Node.Ping);

		// The following endpoints require anonymous access to load the index.html page.
		// If the endpoints are secured, then the user will not be able to logged into EventStoreDB
		policy.AllowAnonymous(Operations.Node.Information.Read);
		policy.AllowAnonymous(Operations.Node.StaticContent);
		policy.AllowAnonymous(Operations.Node.Redirect);

		Action<OperationDefinition> addToPolicy = _allowAnonymousEndpointAccess
			? op => policy.AllowAnonymous(op)
			: op => policy.RequireAuthenticated(op);

		if (_overrideAnonymousGossipEndpointAccess) {
			policy.AllowAnonymous(Operations.Node.Gossip.ClientRead);
		} else {
			addToPolicy(Operations.Node.Gossip.ClientRead);
		}

		addToPolicy(Operations.Node.Options);
		addToPolicy(Operations.Node.Statistics.Read);
		addToPolicy(Operations.Node.Statistics.Replication);
		addToPolicy(Operations.Node.Statistics.Tcp);
		addToPolicy(Operations.Node.Statistics.Custom);

		policy.AddMatchAnyAssertion(Operations.Node.Information.Subsystems, Grant.Allow, OperationsOrAdmins);
		policy.AddMatchAnyAssertion(Operations.Node.Information.Histogram, Grant.Allow, OperationsOrAdmins);
		policy.AddMatchAnyAssertion(Operations.Node.Information.Options, Grant.Allow, OperationsOrAdmins);

		var isSystem = new MultipleClaimMatchAssertion(Grant.Allow, MultipleMatchMode.All,
			SystemAccounts.System.Claims.ToArray());
		policy.Add(Operations.Node.Elections.Prepare, isSystem);
		policy.Add(Operations.Node.Elections.PrepareOk, isSystem);
		policy.Add(Operations.Node.Elections.ViewChange, isSystem);
		policy.Add(Operations.Node.Elections.ViewChangeProof, isSystem);
		policy.Add(Operations.Node.Elections.Proposal, isSystem);
		policy.Add(Operations.Node.Elections.Accept, isSystem);
		policy.Add(Operations.Node.Elections.LeaderIsResigning, isSystem);
		policy.Add(Operations.Node.Elections.LeaderIsResigningOk, isSystem);
		policy.Add(Operations.Node.Gossip.Update, isSystem);
		policy.Add(Operations.Node.Gossip.Read, isSystem);

		policy.AddMatchAnyAssertion(Operations.Node.Shutdown, Grant.Allow, OperationsOrAdmins);
		policy.AddMatchAnyAssertion(Operations.Node.ReloadConfiguration, Grant.Allow, OperationsOrAdmins);
		policy.AddMatchAnyAssertion(Operations.Node.MergeIndexes, Grant.Allow, OperationsOrAdmins);
		policy.AddMatchAnyAssertion(Operations.Node.SetPriority, Grant.Allow, OperationsOrAdmins);
		policy.AddMatchAnyAssertion(Operations.Node.Resign, Grant.Allow, OperationsOrAdmins);
		policy.RequireAuthenticated(Operations.Node.Login);
		policy.AddMatchAnyAssertion(Operations.Node.Scavenge.Start, Grant.Allow, OperationsOrAdmins);
		policy.AddMatchAnyAssertion(Operations.Node.Scavenge.Stop, Grant.Allow, OperationsOrAdmins);
		policy.AddMatchAnyAssertion(Operations.Node.Scavenge.Read, Grant.Allow, OperationsOrAdmins);
		policy.Add(Operations.Node.Redaction.SwitchChunk, isSystem);
		policy.Add(Operations.Node.Transform.Set, isSystem);

		var subscriptionAccess =
			new AndAssertion(
				new RequireAuthenticatedAssertion(),
				new RequireStreamReadAssertion(legacyStreamAssertion));

		policy.RequireAuthenticated(Operations.Subscriptions.Statistics);
		policy.AddMatchAnyAssertion(Operations.Subscriptions.Create, Grant.Allow, OperationsOrAdmins);
		policy.AddMatchAnyAssertion(Operations.Subscriptions.Update, Grant.Allow, OperationsOrAdmins);
		policy.AddMatchAnyAssertion(Operations.Subscriptions.Delete, Grant.Allow, OperationsOrAdmins);
		policy.AddMatchAnyAssertion(Operations.Subscriptions.Restart, Grant.Allow, OperationsOrAdmins);
		policy.Add(Operations.Subscriptions.ProcessMessages, subscriptionAccess);
		policy.AddMatchAnyAssertion(Operations.Subscriptions.ReplayParked, Grant.Allow, OperationsOrAdmins);

		IAssertion streamAssertion = _allowAnonymousStreamAccess
			? legacyStreamAssertion
			: new AndAssertion(
				new RequireAuthenticatedAssertion(),
				legacyStreamAssertion);

		policy.Add(Operations.Streams.Read, streamAssertion);
		policy.Add(Operations.Streams.Write, streamAssertion);
		policy.Add(Operations.Streams.Delete, streamAssertion);
		policy.Add(Operations.Streams.MetadataRead, streamAssertion);
		policy.Add(Operations.Streams.MetadataWrite, streamAssertion);

		var matchUsername = new ClaimValueMatchesParameterValueAssertion(ClaimTypes.Name,
			Operations.Users.Parameters.UserParameterName, Grant.Allow);
		var isAdmin = new MultipleClaimMatchAssertion(Grant.Allow, MultipleMatchMode.Any, Admins);
		policy.Add(Operations.Users.List, isAdmin);
		policy.Add(Operations.Users.Create, isAdmin);
		policy.Add(Operations.Users.Delete, isAdmin);
		policy.Add(Operations.Users.Update, isAdmin);
		policy.Add(Operations.Users.Enable, isAdmin);
		policy.Add(Operations.Users.Disable, isAdmin);
		policy.Add(Operations.Users.ResetPassword, isAdmin);
		policy.RequireAuthenticated(Operations.Users.CurrentUser);
		policy.Add(Operations.Users.Read, new OrAssertion(isAdmin, matchUsername));
		policy.Add(Operations.Users.ChangePassword, matchUsername);


		policy.RequireAuthenticated(Operations.Projections.List);

		policy.RequireAuthenticated(Operations.Projections.Abort);
		policy.RequireAuthenticated(Operations.Projections.Create);
		policy.RequireAuthenticated(Operations.Projections.DebugProjection);
		policy.AddMatchAnyAssertion(Operations.Projections.Delete, Grant.Allow, OperationsOrAdmins);
		policy.RequireAuthenticated(Operations.Projections.Disable);
		policy.RequireAuthenticated(Operations.Projections.Enable);
		policy.RequireAuthenticated(Operations.Projections.Read);
		policy.AddMatchAnyAssertion(Operations.Projections.ReadConfiguration, Grant.Allow, OperationsOrAdmins);
		policy.RequireAuthenticated(Operations.Projections.Reset);
		policy.RequireAuthenticated(Operations.Projections.Update);
		policy.AddMatchAnyAssertion(Operations.Projections.UpdateConfiguration, Grant.Allow, OperationsOrAdmins);
		policy.RequireAuthenticated(Operations.Projections.State);
		policy.RequireAuthenticated(Operations.Projections.Status);
		policy.RequireAuthenticated(Operations.Projections.Statistics);
		policy.AddMatchAnyAssertion(Operations.Projections.Restart, Grant.Allow, OperationsOrAdmins);

		return new StaticPolicySelector(policy.AsReadOnly());
	}
}
