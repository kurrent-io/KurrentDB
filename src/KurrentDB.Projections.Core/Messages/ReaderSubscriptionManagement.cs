// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;

namespace KurrentDB.Projections.Core.Messages;

public static partial class ReaderSubscriptionManagement {
	[DerivedMessage]
	public abstract partial class ReaderSubscriptionManagementMessage(Guid subscriptionId) : Message {
		public Guid SubscriptionId { get; } = subscriptionId;
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscriptionManagement)]
	public partial class Subscribe(
		Guid subscriptionId,
		CheckpointTag from,
		IReaderStrategy readerStrategy,
		ReaderSubscriptionOptions readerSubscriptionOptions)
		: ReaderSubscriptionManagementMessage(subscriptionId) {
		public CheckpointTag FromPosition { get; } = Ensure.NotNull(from);
		public IReaderStrategy ReaderStrategy { get; } = Ensure.NotNull(readerStrategy);
		public ReaderSubscriptionOptions Options { get; } = readerSubscriptionOptions;
	}

	[DerivedMessage(ProjectionMessage.ReaderSubscriptionManagement)]
	public partial class Pause(Guid subscriptionId) : ReaderSubscriptionManagementMessage(subscriptionId);

	[DerivedMessage(ProjectionMessage.ReaderSubscriptionManagement)]
	public partial class Resume(Guid subscriptionId) : ReaderSubscriptionManagementMessage(subscriptionId);

	[DerivedMessage(ProjectionMessage.ReaderSubscriptionManagement)]
	public partial class Unsubscribe(Guid subscriptionId) : ReaderSubscriptionManagementMessage(subscriptionId);
}
