using System;
using EventStore.Core.Services.Transport.Grpc;

// ReSharper disable CheckNamespace
namespace EventStore.Client.Streams {
	// ReSharper restore CheckNamespace
	partial class ReadReq {
		partial class Types {
			partial class Options {
				partial class Types {
					partial class StreamOptions {
						internal StreamRevision ToStreamRevision() => RevisionOptionCase switch {
							RevisionOptionOneofCase.End => StreamRevision.End,
							RevisionOptionOneofCase.Start => StreamRevision.Start,
							RevisionOptionOneofCase.Revision => new StreamRevision(Revision),
							_ => throw new InvalidOperationException()
						};

						internal StreamRevision? ToSubscriptionStreamRevision() => RevisionOptionCase switch {
							RevisionOptionOneofCase.End => StreamRevision.End,
							RevisionOptionOneofCase.Start => null,
							RevisionOptionOneofCase.Revision => new StreamRevision(Revision),
							_ => throw new InvalidOperationException()
						};
					}

					partial class AllOptions {
						internal Core.Services.Transport.Grpc.Position ToPosition() => AllOptionCase switch {
							AllOptionOneofCase.End => Core.Services.Transport.Grpc.Position.End,
							AllOptionOneofCase.Start => Core.Services.Transport.Grpc.Position.Start,
							AllOptionOneofCase.Position => new Core.Services.Transport.Grpc.Position(Position.CommitPosition,
								Position.PreparePosition),
							_ => throw new InvalidOperationException()
						};

						internal Core.Services.Transport.Grpc.Position? ToSubscriptionPosition() => AllOptionCase switch {
							AllOptionOneofCase.End => Core.Services.Transport.Grpc.Position.End,
							AllOptionOneofCase.Start => null,
							AllOptionOneofCase.Position => new Core.Services.Transport.Grpc.Position(Position.CommitPosition,
								Position.PreparePosition),
							_ => throw new InvalidOperationException()
						};
					}
				}
			}
		}
	}
}
