// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNet.Testcontainers.Builders;
using Grpc.Net.ClientFactory;
using Humanizer;
using KurrentDB.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
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

public record NodeShimOptions {
	public bool Insecure { get; set; }
	public NodeType NodeType { get; set; } = NodeType.Embedded;
	public ContainerOptions Container { get; set; } = new();
	public EmbeddedOptions Embedded { get; set; } = new();
	public ExternalOptions External { get; set; } = new();

	public record ContainerOptions {
		public bool CleanUp { get; set; } = true;
		public string Registry { get; set; } = "docker.kurrent.io/kurrent-preview";
		public string Repository { get; set; } = "kurrentdb";
		public string Tag { get; set; } = "nightly";
	}

	public record EmbeddedOptions {
	}

	public record ExternalOptions {
		public string Host { get; set; } = "localhost";
		public int Port { get; set; } = 2113;
	}
}

// Abstracts away whether the node is containerized, embedded, or external.
public sealed class NodeShim : IAsyncInitializer, IAsyncDisposable {
	const string ConfigurationPrefix = "Node";

	public NodeShim() {
		var options = TestContext.Configuration.Bind<NodeShimOptions>(ConfigurationPrefix);

		Console.WriteLine("NodeType: {0}", options.NodeType);
		Node = options.NodeType switch {
			NodeType.Container => new ContainerNode(options),
			NodeType.Embedded => new EmbeddedNode(),
			NodeType.External => new ExternalNode(options),
			var unknown => throw new InvalidOperationException($"Unknown node type: {unknown}"),
		};
	}

	public INode Node { get; }

	public Task InitializeAsync() =>
		Node.InitializeAsync();

	public ValueTask DisposeAsync() =>
		Node.DisposeAsync();

	public sealed class ContainerNode(NodeShimOptions options) : INode {
		const int ContainerPort = 2113;

		readonly Disposables _disposables = new();

		public ClusterVNodeOptions ClusterVNodeOptions => throw this.Skip();
		public IServiceProvider Services => throw this.Skip();
		public Uri Uri { get; private set; } = null!;

		public async Task InitializeAsync() {
			var o = options.Container;

			var container = new ContainerBuilder()
				.WithImage($"{o.Registry}/{o.Repository}:{o.Tag}")
				.WithPortBinding(ContainerPort, assignRandomHostPort: true)
				.WithEnvironment(new Dictionary<string, string> {
					{ "KURRENTDB_MEM_DB", "true" },
					{ "KURRENTDB_INSECURE", $"{options.Insecure}" },
					{ "KURRENTDB__CONNECTORS__DATA_PROTECTION__TOKEN", "the-token" },
				})
				.WithCleanUp(o.CleanUp)
				.Build()
				.DisposeAsyncWith(_disposables);

			await container.StartAsync();

			Uri = new($"http://localhost:{container.GetMappedPublicPort(ContainerPort)}");

			//qq wait for the container to be ready, there is a better way
			await Task.Delay(10000);
		}

		public async ValueTask DisposeAsync() {
			await _disposables.DisposeAsync();
		}
	}

	public sealed class ExternalNode(NodeShimOptions options) : INode {
		public ClusterVNodeOptions ClusterVNodeOptions => throw this.Skip();
		public IServiceProvider Services => throw this.Skip();
		public Uri Uri { get; } = new(
			(options.Insecure ? "http" : "https") +
			$"://{options.External.Host}:{options.External.Port}");

		public ValueTask DisposeAsync() =>
			ValueTask.CompletedTask;

		public Task InitializeAsync() =>
			Task.CompletedTask;
	}

	public sealed class EmbeddedNode() : INode {
		readonly ClusterVNodeApp _node = new(ConfigureServices, ConfigurationOverrides);

		static readonly Dictionary<string, object?> ConfigurationOverrides = new() {
			{ "KurrentDB:Application:MaxAppendEventSize", 4.Megabytes().Bytes },
			{ "KurrentDB:Application:MaxAppendSize", 24.Megabytes().Bytes },
			{ "KurrentDB:Connectors:DataProtection:Token", "the-token" },
		};

		public ClusterVNodeOptions ClusterVNodeOptions => _node.ServerOptions;
		public IServiceProvider Services => _node.Services;
		public Uri Uri => _node.Services.GetServerLocalAddress(https: false);

		// moved from ClusterVNodeTestContext
		static void ConfigureServices(ClusterVNodeOptions options, IServiceCollection services) {
			services
				.AddSingleton<ILoggerFactory, ToolkitTestLoggerFactory>()
				//.AddTestLogging()
				.AddTestTimeProvider();

			services.ConfigureAll<HttpClientFactoryOptions>(factory => {
				// //this must be switched on before creation of the HttpMessageHandler
				// AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

				factory.HttpMessageHandlerBuilderActions.Add(builder => {
					if (builder.PrimaryHandler is SocketsHttpHandler { } handler) {
						handler.AutomaticDecompression = DecompressionMethods.All;
						handler.SslOptions = new() { RemoteCertificateValidationCallback = (_, _, _, _) => true };
					}
				});
			});

			services.ConfigureAll<GrpcClientFactoryOptions>(factory =>
				factory.ChannelOptionsActions.Add(channel => {
					channel.UnsafeUseInsecureChannelCallCredentials = true;
				})
			);
		}

		public async Task InitializeAsync() {
			await _node.Start();
		}

		public async ValueTask DisposeAsync() {
			await _node.DisposeAsync();
		}
	}
}

file static class Extensions {
	public static SkipTestException Skip(this INode node) =>
		new($"Test cannot be run against a {node.GetType().Name}");
}
