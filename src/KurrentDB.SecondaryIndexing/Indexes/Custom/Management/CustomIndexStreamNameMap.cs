// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public class CustomIndexStreamNameMap : StreamNameMap {
	public CustomIndexStreamNameMap() {
		Register<CustomIndexId>(id => new StreamName(CustomIndexHelpers.GetManagementStreamName(id)));
	}
}
