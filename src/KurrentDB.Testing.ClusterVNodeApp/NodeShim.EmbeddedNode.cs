// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using Grpc.Net.ClientFactory;
using Humanizer;
using KurrentDB.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Testing;

public sealed partial class NodeShim {
	sealed class EmbeddedNode(NodeShimOptions options) : INode {
		readonly ClusterVNodeApp _node = new(
			ConfigureServices,
			new() {
				{ "KurrentDB:Application:Insecure", options.Insecure },
				{ "KurrentDB:Application:MaxAppendEventSize", 4.Megabytes().Bytes },
				{ "KurrentDB:Application:MaxAppendSize", 24.Megabytes().Bytes },
				{ "KurrentDB:Certificate:TrustedRootCertificatesPath", options.Embedded.TrustedRootCertificatesPath },
				{ "KurrentDB:CertificateFile:CertificateFile", options.Embedded.CertificateFile },
				{ "KurrentDB:CertificateFile:CertificatePrivateKeyFile", options.Embedded.CertificatePrivateKeyFile },
				{ "KurrentDB:Connectors:DataProtection:Token", "the-token" },
			});

		public ClusterVNodeOptions ClusterVNodeOptions => _node.ServerOptions;
		public IServiceProvider Services => _node.Services;
		public Uri Uri => _node.Services.GetServerLocalAddress(https: !options.Insecure);

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
						handler.SslOptions = new() {
							RemoteCertificateValidationCallback = (_, _, _, _) => true
						};
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
