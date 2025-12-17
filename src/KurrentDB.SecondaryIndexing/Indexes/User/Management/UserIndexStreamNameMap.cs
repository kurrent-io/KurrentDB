// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;

namespace KurrentDB.SecondaryIndexing.Indexes.User.Management;

public class UserIndexStreamNameMap : StreamNameMap {
	public UserIndexStreamNameMap() {
		Register<UserIndexId>(id => new StreamName(UserIndexHelpers.GetManagementStreamName(id)));
	}
}
