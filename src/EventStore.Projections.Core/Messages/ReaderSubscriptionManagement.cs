// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Messages;

public static partial class ReaderSubscriptionManagement {
	[DerivedMessage]
	public abstract partial class ReaderSubscriptionManagementMessage : Message {
		private readonly Guid _subscriptionId;

		protected ReaderSubscriptionManagementMessage(Guid subscriptionId) {
			_subscriptionId = subscriptionId;
		}

		public Guid SubscriptionId {
			get { return _subscriptionId; }
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscriptionManagement)]
	public partial class Subscribe : ReaderSubscriptionManagementMessage {
		private readonly CheckpointTag _fromPosition;
		private readonly IReaderStrategy _readerStrategy;
		private readonly ReaderSubscriptionOptions _options;

		public Subscribe(
			Guid subscriptionId, CheckpointTag from,
			IReaderStrategy readerStrategy, ReaderSubscriptionOptions readerSubscriptionOptions) : base(
			subscriptionId) {
			if (@from == null) throw new ArgumentNullException("from");
			if (readerStrategy == null) throw new ArgumentNullException("readerStrategy");
			_fromPosition = @from;
			_readerStrategy = readerStrategy;
			_options = readerSubscriptionOptions;
		}

		public CheckpointTag FromPosition {
			get { return _fromPosition; }
		}

		public IReaderStrategy ReaderStrategy {
			get { return _readerStrategy; }
		}

		public ReaderSubscriptionOptions Options {
			get { return _options; }
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscriptionManagement)]
	public partial class Pause : ReaderSubscriptionManagementMessage {
		public Pause(Guid subscriptionId)
			: base(subscriptionId) {
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscriptionManagement)]
	public partial class Resume : ReaderSubscriptionManagementMessage {
		public Resume(Guid subscriptionId)
			: base(subscriptionId) {
		}
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscriptionManagement)]
	public partial class Unsubscribe : ReaderSubscriptionManagementMessage {
		public Unsubscribe(Guid subscriptionId)
			: base(subscriptionId) {
		}
	}
}
