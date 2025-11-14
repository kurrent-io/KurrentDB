// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core;
using Microsoft.Extensions.Configuration;
using TUnit.Core.Exceptions;
using TUnit.Core.Interfaces;

namespace KurrentDB.Testing;

public enum NodeType {
	None,
	Container,
	Embedded,
	External,
};

public interface INode : IAsyncInitializer, IAsyncDisposable {
	ClusterVNodeOptions ClusterVNodeOptions { get; }
	IServiceProvider Services { get; }
	Uri Uri { get; }
}

static class INodeExtensions {
	public static SkipTestException Skip(this INode node) =>
		new($"Test cannot be run against a {node.GetType().Name}");
}

// Abstracts away whether the node is containerized, embedded, or external.
public sealed partial class NodeShim : IAsyncInitializer, IAsyncDisposable {
	const string ConfigurationPrefix = "Node";

	public INode Node { get; private set; } = null!;

	public NodeShimOptions NodeShimOptions { get; private set; } = null!;

	public async Task InitializeAsync() {
		NodeShimOptions = ToolkitTestEnvironment.Configuration?
			.GetSection(ConfigurationPrefix)
			.Get<NodeShimOptions>() ?? new();

		Console.WriteLine("NodeType: {0}", NodeShimOptions.NodeType);

		Node = NodeShimOptions.NodeType switch {
			NodeType.Container => new ContainerNode(NodeShimOptions),
			NodeType.Embedded => new EmbeddedNode(NodeShimOptions),
			NodeType.External => new ExternalNode(NodeShimOptions),
			var unknown => throw new InvalidOperationException($"Unknown node type: {unknown}"),
		};

		await Node.InitializeAsync();
	}

	public ValueTask DisposeAsync() =>
		Node.DisposeAsync();
}
