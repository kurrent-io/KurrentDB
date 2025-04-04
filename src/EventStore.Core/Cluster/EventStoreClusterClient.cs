using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Metrics;
using EventStore.Core.Settings;
using Grpc.Net.Client;
using Serilog.Extensions.Logging;

namespace EventStore.Core.Cluster {

	public partial class EventStoreClusterClient : IDisposable {
		private readonly EventStore.Cluster.Gossip.GossipClient _gossipClient;
		private readonly EventStore.Cluster.Elections.ElectionsClient _electionsClient;

		private readonly GrpcChannel _channel;
		private readonly IPublisher _bus;
		private readonly string _clusterDns;
		private readonly IDurationTracker _gossipSendTracker;
		private readonly IDurationTracker _gossipGetTracker;

		public bool Disposed { get; private set; }

		public EventStoreClusterClient(string uriScheme, EndPoint endpoint, string clusterDns,
			IPublisher bus, CertificateDelegates.ServerCertificateValidator serverCertValidator,
			Func<X509Certificate> clientCertificateSelector,
			IDurationTracker gossipSendTracker,
			IDurationTracker gossipGetTracker) {

			HttpMessageHandler httpMessageHandler = null;
			_clusterDns = clusterDns;
			if (uriScheme == Uri.UriSchemeHttps) {
				var socketsHttpHandler = new SocketsHttpHandler {
					SslOptions = {
						CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
						RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => {
							var (isValid, error) = serverCertValidator(certificate, chain, errors, endpoint.GetOtherNames());
							if (!isValid && error != null) {
								Log.Error("Server certificate validation error: {e}", error);
							}

							return isValid;
						},
						LocalCertificateSelectionCallback = delegate {
							return clientCertificateSelector();
						}
					},
					PooledConnectionLifetime = ESConsts.HttpClientConnectionLifeTime
				};

				httpMessageHandler = socketsHttpHandler;
			} else if (uriScheme == Uri.UriSchemeHttp) {
				httpMessageHandler = new SocketsHttpHandler();
			}

			var address = new UriBuilder(uriScheme, endpoint.GetHost(), endpoint.GetPort()).Uri;
			_channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions {
				HttpClient = new HttpClient(httpMessageHandler) {
					Timeout = Timeout.InfiniteTimeSpan,
					DefaultRequestVersion = new Version(2, 0)
				},
				LoggerFactory = new SerilogLoggerFactory()
			});
			var callInvoker = _channel.CreateCallInvoker();
			_gossipClient = new EventStore.Cluster.Gossip.GossipClient(callInvoker);
			_electionsClient = new EventStore.Cluster.Elections.ElectionsClient(callInvoker);
			_bus = bus;
			_gossipSendTracker = gossipSendTracker;
			_gossipGetTracker = gossipGetTracker;
		}

		public void Dispose() {
			if (Disposed)
				return;
			_channel.Dispose();
			Disposed = true;
		}
	}
}
