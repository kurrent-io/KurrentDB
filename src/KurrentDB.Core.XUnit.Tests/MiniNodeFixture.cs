// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.Helpers;

namespace KurrentDB.Core.XUnit.Tests;

public class MiniNodeFixture<T> : DirectoryFixture<T> {
	public MiniNode<LogFormat.V2, string> MiniNode { get; private set; }

	public override async Task InitializeAsync() {
		await base.InitializeAsync();
		MiniNode = new MiniNode<LogFormat.V2, string>(Directory, inMemDb: false);
		await MiniNode.Start();
	}

	public override async Task DisposeAsync() {
		await MiniNode.Shutdown(keepDb: false);
		await base.DisposeAsync();
	}
}
