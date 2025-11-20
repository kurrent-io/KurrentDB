// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core;

namespace KurrentDB.Testing;

public sealed partial class NodeShim {
	sealed class ExternalNode(NodeShimOptions options) : INode {
		public ClusterVNodeOptions ClusterVNodeOptions => throw this.Skip();
		public IServiceProvider Services => throw this.Skip();
		public Uri Uri { get; } = new UriBuilder {
			Scheme = options.Insecure ? "http" : "https",
			Host = options.External.Host,
			Port = options.External.Port,
		}.Uri;

		public ValueTask DisposeAsync() =>
			ValueTask.CompletedTask;

		public Task InitializeAsync() =>
			Task.CompletedTask;
	}
}
