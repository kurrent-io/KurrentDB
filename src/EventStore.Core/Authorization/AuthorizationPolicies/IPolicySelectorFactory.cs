// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Bus;

namespace EventStore.Core.Authorization.AuthorizationPolicies;

// TODO: Move this to EventStore.Plugins when we're decoupling the StreamPolicies Plugin
public interface IPolicySelectorFactory {
	string CommandLineName { get; }
	IPolicySelector Create(IPublisher publisher);
	Task<bool> Enable();
	Task Disable();
}
