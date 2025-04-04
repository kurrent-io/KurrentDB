using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using EventStore.Common.Configuration;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.LogV2;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Http;
using EventStore.Core.Services.Transport.Http.Authentication;
using EventStore.Core.Tests.Authorization;
using EventStore.Core.Tests.Common.ClusterNodeOptionsTests;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Plugins.Subsystems;
using EventStore.Transport.Http.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using HttpResponse = EventStore.Transport.Http.HttpResponse;

namespace EventStore.Core.Tests.Services.Transport.Http {
	public class PortableServer {
		public bool IsListening {
			get { return _service.IsListening; }
		}

		public IHttpClient BuiltInClient {
			get { return _client; }
		}

		private InMemoryBus _bus;
		private KestrelHttpService _service;
		private MultiQueuedHandler _multiQueuedHandler;
		private HttpAsyncClient _client;
		private readonly TimeSpan _timeout;
		private readonly IPEndPoint _serverEndPoint;
		private TestServer _server;
		private HttpMessageHandler _httpMessageHandler;
		private InternalDispatcherEndpoint _internalDispatcher;

		public PortableServer(IPEndPoint serverEndPoint, int timeout = 10000) {
			Ensure.NotNull(serverEndPoint, "serverEndPoint");
			_serverEndPoint = serverEndPoint;
			_timeout = TimeSpan.FromMilliseconds(timeout);
		}

		public void SetUp(Action<IHttpService> bootstrap = null) {
			_bus = new InMemoryBus($"bus_{_serverEndPoint.Port}");
			var pipelineBus = InMemoryBus.CreateTest();
			var queue = new QueuedHandlerThreadPool(pipelineBus, "Test", new QueueStatsManager(), new(), true, TimeSpan.FromMilliseconds(50));
			_multiQueuedHandler = new MultiQueuedHandler(new IQueuedHandler[] { queue }, null);
			_multiQueuedHandler.Start();

			_service = new KestrelHttpService(ServiceAccessibility.Private, _bus, new NaiveUriRouter(),
				_multiQueuedHandler, false, null, 0, false, _serverEndPoint);
			_internalDispatcher = new InternalDispatcherEndpoint(queue, _multiQueuedHandler);
			_bus.Subscribe(_internalDispatcher);
			KestrelHttpService.CreateAndSubscribePipeline(pipelineBus);
			bootstrap?.Invoke(_service);
			_server = new TestServer(
				new WebHostBuilder()
					.UseStartup(new ClusterVNodeStartup<string>(Array.Empty<ISubsystem>(), queue, queue, _bus, _multiQueuedHandler,
						new TestAuthenticationProvider(),
						new IHttpAuthenticationProvider[] {
							new BasicHttpAuthenticationProvider(new TestAuthenticationProvider()),
							new AnonymousHttpAuthenticationProvider(),
						}, new TestAuthorizationProvider(),
						new FakeReadIndex<LogFormat.V2, string>(_ => false, new LogV2SystemStreams()),
						1024 * 1024, _timeout, expiryStrategy: null, _service,
						new MetricsConfiguration(),
						new Trackers(),
						null)));
			_httpMessageHandler = _server.CreateHandler();
			_client = new HttpAsyncClient(_timeout, _httpMessageHandler);

			HttpBootstrap.Subscribe(_bus, _service);
		}

		public void TearDown() {
			HttpBootstrap.Unsubscribe(_bus, _service);

			_service.Shutdown();
			_httpMessageHandler?.Dispose();
			_client.Dispose();
			_multiQueuedHandler.Stop();
			_server?.Dispose();
		}

		public void Publish(Message message) {
			_bus.Publish(message);
		}

		public Tuple<bool, string> StartServiceAndSendRequest(string requestUrl,
			Func<HttpResponse, bool> verifyResponse) {
			_bus.Publish(new SystemMessage.SystemInit());

			var signal = new AutoResetEvent(false);
			var success = false;
			var error = string.Empty;

			_client.Get(requestUrl,
				response => {
					success = verifyResponse(response);
					signal.Set();
				},
				exception => {
					success = false;
					error = exception.Message;
					signal.Set();
				});

			signal.WaitOne();
			return new Tuple<bool, string>(success, error);
		}

		private class HttpServiceStartup : IStartup {
			private readonly RequestDelegate _dispatcher;
			private readonly KestrelHttpService _httpService;

			public HttpServiceStartup(RequestDelegate dispatcher, KestrelHttpService httpService) {
				_dispatcher = dispatcher;
				_httpService = httpService;
			}
			public IServiceProvider ConfigureServices(IServiceCollection services) => services
				.AddRouting()
				.BuildServiceProvider();

			public void Configure(IApplicationBuilder app) => app.UseLegacyHttp(_dispatcher, _httpService);
		}
	}
}
