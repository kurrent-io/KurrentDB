using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;

namespace EventStore.Projections.Core.Services {
	public class PublishSubscribeDispatcher<TGuid, TSubscribeRequest, TControlMessageBase, TResponseBase>
		where TSubscribeRequest : Message
		where TControlMessageBase : Message
		where TResponseBase : Message {
		//NOTE: this class is not intended to be used from multiple threads, 
		//however we support count requests from other threads for statistics purposes

		private readonly Dictionary<TGuid, object> _map = new Dictionary<TGuid, object>();
		private readonly IPublisher _publisher;
		private readonly Func<TSubscribeRequest, TGuid> _getRequestCorrelationId;
		private readonly Func<TResponseBase, TGuid> _getResponseCorrelationId;

		public PublishSubscribeDispatcher(
			IPublisher publisher, Func<TSubscribeRequest, TGuid> getRequestCorrelationId,
			Func<TResponseBase, TGuid> getResponseCorrelationId) {
			_publisher = publisher;
			_getRequestCorrelationId = getRequestCorrelationId;
			_getResponseCorrelationId = getResponseCorrelationId;
		}

		public TGuid PublishSubscribe(TSubscribeRequest request, object subscriber) {
			return PublishSubscribe(_publisher, request, subscriber);
		}

		public TGuid PublishSubscribe(IPublisher publisher, TSubscribeRequest request, object subscriber) {
			//TODO: expiration?
			TGuid requestCorrelationId;
			lock (_map) {
				requestCorrelationId = _getRequestCorrelationId(request);
				_map.Add(requestCorrelationId, subscriber);
			}

			publisher.Publish(request);
			//NOTE: the following condition is required as publishing the message could also process the message 
			// and the correlationId is already invalid here (as subscriber unsubscribed)
			return _map.ContainsKey(requestCorrelationId) ? requestCorrelationId : default(TGuid);
		}

		public void Publish(TControlMessageBase request) {
			Publish(_publisher, request);
		}

		public void Publish(IPublisher publisher, TControlMessageBase request) {
			publisher.Publish(request);
		}


		public void Cancel(TGuid requestId) {
			lock (_map)
				_map.Remove(requestId);
		}

		public void CancelAll() {
			lock (_map)
				_map.Clear();
		}

		public IHandle<T> CreateSubscriber<T>() where T : TResponseBase {
			return new Subscriber<T>(this);
		}

		private class Subscriber<T> : IHandle<T> where T : TResponseBase {
			private readonly PublishSubscribeDispatcher<TGuid, TSubscribeRequest, TControlMessageBase, TResponseBase>
				_host;

			public Subscriber(
				PublishSubscribeDispatcher<TGuid, TSubscribeRequest, TControlMessageBase, TResponseBase> host) {
				_host = host;
			}

			public void Handle(T message) {
				_host.Handle<T>(message);
			}
		}

		public bool Handle<T>(T message) where T : TResponseBase {
			var correlationId = _getResponseCorrelationId(message);
			lock (_map) {
				object subscriber;
				if (_map.TryGetValue(correlationId, out subscriber)) {
					var h = subscriber as IHandle<T>;
					if (h != null)
						h.Handle(message);
					return true;
				}
			}

			return false;
		}

		public void Subscribed(TGuid correlationId, object subscriber) {
			lock (_map) {
				_map.Add(correlationId, subscriber);
			}
		}
	}
}
