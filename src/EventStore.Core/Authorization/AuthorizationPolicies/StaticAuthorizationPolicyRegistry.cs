// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System.Linq;
using System.Threading.Tasks;

namespace EventStore.Core.Authorization.AuthorizationPolicies;

public class StaticAuthorizationPolicyRegistry : IAuthorizationPolicyRegistry {
	public Task Start() => Task.CompletedTask;
	public Task Stop() => Task.CompletedTask;
	private readonly IPolicySelector[] _policySelectors;
	public ReadOnlyPolicy[] EffectivePolicies => _policySelectors.Select(x => x.Select()).ToArray();

	public StaticAuthorizationPolicyRegistry(IPolicySelector[] policySelectors) {
		_policySelectors = policySelectors;
	}
}
