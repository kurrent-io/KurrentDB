// ReSharper disable CheckNamespace
namespace EventStore.Client.Streams {
	// ReSharper restore CheckNamespace
	partial class ReadResp {
		partial class Types {
			partial class ReadEvent {
				public Types.RecordedEvent OriginalEvent => Link ?? Event;
			}
		}
	}
}
