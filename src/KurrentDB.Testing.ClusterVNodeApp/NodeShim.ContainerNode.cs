// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNet.Testcontainers.Builders;
using KurrentDB.Core;

namespace KurrentDB.Testing;

public sealed partial class NodeShim {
	sealed class ContainerNode(NodeShimOptions options) : INode {
		const int ContainerPort = 2113;

		readonly Disposables _disposables = new();

		public ClusterVNodeOptions ClusterVNodeOptions => throw this.Skip();
		public IServiceProvider Services => throw this.Skip();
		public Uri Uri { get; private set; } = null!;

		public async Task InitializeAsync() {
			var o = options.Container;
			var nameSuffix = Guid.NewGuid();

			// TODO: consider creating certificates only if they are missing or expired
			var volume = new VolumeBuilder()
				.WithName("KurrentDB.Testing.Certs")
				.WithCleanUp(true)
				.Build()
				.DisposeAsyncWith(_disposables);

			var volumeProvisionerContainer = new ContainerBuilder()
				.WithName($"KurrentDB.Testing.VolumeProvisioner-{nameSuffix}")
				.WithImage("hasnat/volumes-provisioner")
				.WithVolumeMount(volume, "/tmp/certs")
				.WithEnvironment(new Dictionary<string, string> {
					{ "PROVISION_DIRECTORIES", "1000:1000:0755:/tmp/certs" },
				})
				.WithCleanUp(true)
				.Build()
				.DisposeAsyncWith(_disposables);

			var genCertContainer = new ContainerBuilder()
				.WithName($"KurrentDB.Testing.GenCerts-{nameSuffix}")
				.WithImage("eventstore/es-gencert-cli:1.0.2")
				.WithVolumeMount(volume, "/certs")
				.WithEntrypoint("bash")
				.WithCommand(
					"-c",
					"mkdir -p ./certs && cd /certs" +
					" && es-gencert-cli create-ca" +
					" && es-gencert-cli create-node -out ./node1 -ip-addresses 127.0.0.1 -dns-names localhost" +
					" && es-gencert-cli create-node -out ./node2 -ip-addresses 127.0.0.1 -dns-names localhost" +
					" && es-gencert-cli create-node -out ./node3 -ip-addresses 127.0.0.1 -dns-names localhost" +
					" && find . -type f -print0 | xargs -0 chmod 666")
				.WithCleanUp(true)
				.DependsOn(volumeProvisionerContainer)
				.Build()
				.DisposeAsyncWith(_disposables);

			var nodeContainer = new ContainerBuilder()
				.WithName($"KurrentDB.Testing.Node-{nameSuffix}")
				.WithImage($"{o.Registry}/{o.Repository}:{o.Tag}")
				.WithPortBinding(ContainerPort, assignRandomHostPort: true)
				.WithVolumeMount(volume, "/certs")
				.WithEnvironment(new Dictionary<string, string> {
					{ "KURRENTDB_MEM_DB", "true" },
					{ "KURRENTDB_INSECURE", $"{options.Insecure}" },
					{ "KURRENTDB_TRUSTED_ROOT_CERTIFICATES_PATH", "/certs/ca" },
					{ "KURRENTDB_CERTIFICATE_FILE", "/certs/node1/node.crt" },
					{ "KURRENTDB_CERTIFICATE_PRIVATE_KEY_FILE", "/certs/node1/node.key" },
					{ "KURRENTDB__CONNECTORS__DATA_PROTECTION__TOKEN", "the-token" },
				})
				.WithCleanUp(o.CleanUp)
				.DependsOn(genCertContainer)
				.Build()
				.DisposeAsyncWith(_disposables);

			await volumeProvisionerContainer.StartAsync();
			await genCertContainer.StartAsync();
			await nodeContainer.StartAsync();

			Uri = new UriBuilder {
				Scheme = options.Insecure ? "http" : "https",
				Host = "localhost",
				Port = nodeContainer.GetMappedPublicPort(ContainerPort),
			}.Uri;
		}

		public async ValueTask DisposeAsync() {
			await _disposables.DisposeAsync();
		}
	}
}
