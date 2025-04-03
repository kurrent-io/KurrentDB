// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Services.Transport.Common;
using RecordPosition = EventStore.Core.Services.Transport.Common.Position;

// ReSharper disable CheckNamespace
namespace EventStore.Client.Streams;

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
					internal RecordPosition ToPosition() => AllOptionCase switch {
						AllOptionOneofCase.End => RecordPosition.End,
						AllOptionOneofCase.Start => RecordPosition.Start,
						AllOptionOneofCase.Position => new(Position.CommitPosition, Position.PreparePosition),
						_ => throw new InvalidOperationException()
					};

					internal RecordPosition? ToSubscriptionPosition() => AllOptionCase switch {
						AllOptionOneofCase.End => RecordPosition.End,
						AllOptionOneofCase.Start => null,
						AllOptionOneofCase.Position => new RecordPosition(Position.CommitPosition, Position.PreparePosition),
						_ => throw new InvalidOperationException()
					};
				}
			}
		}
	}
}
