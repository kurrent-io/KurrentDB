using System;
using System.Linq;
using System.Net;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.Tests.Http;
using EventStore.Transport.Http;
using NUnit.Framework;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace EventStore.Core.Tests.Services.Transport.Http {
	[TestFixture, Category("LongRunning")]
	public class http_service_should {
		private readonly IPEndPoint _serverEndPoint;
		private readonly PortableServer _portableServer;

		public http_service_should() {
			var port = PortsHelper.GetAvailablePort(IPAddress.Loopback);
			_serverEndPoint = new IPEndPoint(IPAddress.Loopback, port);
			_portableServer = new PortableServer(_serverEndPoint);
		}

		[SetUp]
		public void SetUp() {
			_portableServer.SetUp(HttpBootstrap.RegisterPing);
		}

		[TearDown]
		public void TearDown() {
			_portableServer.TearDown();
		}

		[Test]
		[Category("Network")]
		public void start_after_system_message_system_init_published() {
			Assert.IsFalse(_portableServer.IsListening);
			_portableServer.Publish(new SystemMessage.SystemInit());
			Assert.IsTrue(_portableServer.IsListening);
		}

		[Test]
		[Category("Network")]
		public void ignore_shutdown_message_that_does_not_say_shut_down() {
			_portableServer.Publish(new SystemMessage.SystemInit());
			Assert.IsTrue(_portableServer.IsListening);

			_portableServer.Publish(
				new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), exitProcess: false, shutdownHttp: false));
			Assert.IsTrue(_portableServer.IsListening);
		}

		[Test]
		[Category("Network")]
		public void react_to_shutdown_message_that_cause_process_exit() {
			_portableServer.Publish(new SystemMessage.SystemInit());
			Assert.IsTrue(_portableServer.IsListening);

			_portableServer.Publish(
				new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), exitProcess: true, shutdownHttp: true));
			Assert.IsFalse(_portableServer.IsListening);
		}

		[Test]
		[Category("Network")]
		public void handle_invalid_characters_in_url() {
			var url = _serverEndPoint.ToHttpUrl(EndpointExtensions.HTTP_SCHEMA, "/ping^\"");
			Func<HttpResponse, bool> verifier = response => string.IsNullOrEmpty(response.Body) &&
															response.HttpStatusCode == (int)HttpStatusCode.NotFound;

			var result = _portableServer.StartServiceAndSendRequest(url, verifier);
			Assert.IsTrue(result.Item1, result.Item2);
		}
	}

	[TestFixture, Category("LongRunning")]
	public class when_http_service_has_no_controllers {
		private readonly IPEndPoint _serverEndPoint;
		private readonly PortableServer _portableServer;

		public when_http_service_has_no_controllers() {
			var port = PortsHelper.GetAvailablePort(IPAddress.Loopback);
			_serverEndPoint = new IPEndPoint(IPAddress.Loopback, port);
			_portableServer = new PortableServer(_serverEndPoint);
		}

		[SetUp]
		public void SetUp() {
			_portableServer.SetUp();
		}

		[TearDown]
		public void TearDown() {
			_portableServer.TearDown();
		}

		[Test]
		[Category("Network")]
		public void reply_with_404_to_every_request() {
			var requests = new[] { "/ping", "/streams", "/gossip", "/stuff", "/notfound", "/magic/url.exe" };
			var successes = new bool[requests.Length];
			var errors = new string[requests.Length];
			var signals = new AutoResetEvent[requests.Length];
			for (var i = 0; i < signals.Length; i++)
				signals[i] = new AutoResetEvent(false);

			_portableServer.Publish(new SystemMessage.SystemInit());

			for (var i = 0; i < requests.Length; i++) {
				var i1 = i;
				_portableServer.BuiltInClient.Get(
					_serverEndPoint.ToHttpUrl(EndpointExtensions.HTTP_SCHEMA, requests[i]),
					response => {
						successes[i1] = response.HttpStatusCode == (int)HttpStatusCode.NotFound;
						signals[i1].Set();
					},
					exception => {
						successes[i1] = false;
						errors[i1] = exception.Message;
						signals[i1].Set();
					});
			}

			foreach (var signal in signals)
				signal.WaitOne();

			Assert.IsTrue(successes.All(x => x), string.Join(";", errors.Where(e => !string.IsNullOrEmpty(e))));
		}

	}

	[TestFixture, Category("LongRunning")]
	public class when_http_request_times_out {
		private readonly IPEndPoint _serverEndPoint;
		private readonly PortableServer _portableServer;
		private int _timeout;

		public when_http_request_times_out() {
			_timeout = 2000;
			var port = PortsHelper.GetAvailablePort(IPAddress.Loopback);
			_serverEndPoint = new IPEndPoint(IPAddress.Loopback, port);
			_portableServer = new PortableServer(_serverEndPoint, _timeout);
		}

		[SetUp]
		public void SetUp() {
			_portableServer.SetUp(service => service.SetupController(new TestController(new FakePublisher())));
		}

		[TearDown]
		public void TearDown() {
			_portableServer.TearDown();
		}

		[Test]
		[Category("Network")]
		public void should_throw_an_exception() {
			var sleepFor = _timeout + 1000;
			var url = _serverEndPoint.ToHttpUrl(EndpointExtensions.HTTP_SCHEMA,
				string.Format("/test-timeout?sleepfor={0}", sleepFor));
			Func<HttpResponse, bool> verifier = response => { return true; };
			var result = _portableServer.StartServiceAndSendRequest(url, verifier);
			Assert.IsFalse(result.Item1, "Should not have got a response"); // We should not have got a response
			Assert.That(!string.IsNullOrEmpty(result.Item2), "Error was empty");
		}
	}
}
