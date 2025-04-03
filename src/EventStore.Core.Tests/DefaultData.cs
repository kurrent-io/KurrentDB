// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using EventStore.ClientAPI.SystemData;
using EventStore.Core.Services;

namespace EventStore.Core.Tests;

public class DefaultData {
	public static string AdminUsername = SystemUsers.Admin;
	public static string AdminPassword = SystemUsers.DefaultAdminPassword;
	public static UserCredentials AdminCredentials = new UserCredentials(AdminUsername, AdminPassword);
	public static NetworkCredential AdminNetworkCredentials = new NetworkCredential(AdminUsername, AdminPassword);
	public static ClusterVNodeOptions.DefaultUserOptions DefaultUserOptions = new ClusterVNodeOptions.DefaultUserOptions() {
		DefaultAdminPassword = SystemUsers.DefaultAdminPassword,
		DefaultOpsPassword = SystemUsers.DefaultOpsPassword
	};
}
