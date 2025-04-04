using System;
using EventStore.Core.Bus;

namespace EventStore.Core.Messaging {
	public static class Forwarder {
		/// <summary>
		/// Creates a message handler publishing all incoming messages on destination.  
		/// </summary>
		public static IHandle<T> Create<T>(IPublisher to) where T : Message {
			return new F<T>(to);
		}

		public static IHandle<T> CreateTracing<T>(IPublisher to, string prefix) where T : Message {
			return new FTracing<T>(to, prefix);
		}

		/// <summary>
		/// Creates a message handler publishing all incoming messages onto one of the destinations.  
		/// </summary>
		public static IHandle<T> CreateBalancing<T>(params IPublisher[] to) where T : Message {
			return new Balancing<T>(to);
		}

		class F<T> : IHandle<T> where T : Message {
			private readonly IPublisher _to;

			public F(IPublisher to) {
				_to = to;
			}

			public void Handle(T message) {
				_to.Publish(message);
			}
		}

		class FTracing<T> : IHandle<T> where T : Message {
			private readonly IPublisher _to;
			private readonly string _prefix;

			public FTracing(IPublisher to, string prefix) {
				_to = to;
				_prefix = prefix;
			}

			public void Handle(T message) {
				Console.WriteLine(_prefix + message.GetType().Name);
				_to.Publish(message);
			}
		}

		class Balancing<T> : IHandle<T> where T : Message {
			private readonly IPublisher[] _to;
			private int _last;

			public Balancing(IPublisher[] to) {
				_to = to;
			}

			public void Handle(T message) {
				var last = _last;
				if (last == _to.Length - 1)
					_last = 0;
				else
					_last = last + 1;
				_to[_last].Publish(message);
			}
		}
	}
}
