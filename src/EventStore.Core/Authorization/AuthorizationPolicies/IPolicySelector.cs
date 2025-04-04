// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
namespace EventStore.Core.Authorization.AuthorizationPolicies;

public interface IPolicySelector {
	ReadOnlyPolicy Select();
}
