using System;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Services.Transport.Http.Messages;
using EventStore.Transport.Http;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace EventStore.Core.Services.Transport.Http {
	public class InternalDispatcherEndpoint : IHandle<HttpMessage.PurgeTimedOutRequests> {
		private static readonly ILogger Log = Serilog.Log.ForContext<AuthorizationMiddleware>();
		private readonly IPublisher _inputBus;
		private readonly MultiQueuedHandler _requestsMultiHandler;
		private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
		private readonly IEnvelope _publishEnvelope;
		public InternalDispatcherEndpoint(IPublisher inputBus, MultiQueuedHandler requestsMultiHandler) {

			_inputBus = inputBus;
			_requestsMultiHandler = requestsMultiHandler;
			_publishEnvelope = new PublishEnvelope(inputBus);
		}
		public void Handle(HttpMessage.PurgeTimedOutRequests message) {

			_requestsMultiHandler.PublishToAll(message);

			_inputBus.Publish(
				TimerMessage.Schedule.Create(
					UpdateInterval, _publishEnvelope, message));
		}

		public Task InvokeAsync(HttpContext context) {

			if (InternalHttpHelper.TryGetInternalContext(context, out var manager, out var match, out var tcs)) {
				_requestsMultiHandler.Publish(new AuthenticatedHttpRequestMessage(manager, match));
				return tcs.Task;
			}
			Log.Error("Failed to get internal http components for request {requestId}", context.TraceIdentifier);
			context.Response.StatusCode = HttpStatusCode.InternalServerError;
			return Task.CompletedTask;
		}
	}
}
