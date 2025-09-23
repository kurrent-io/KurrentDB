// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;

namespace KurrentDB.Projections.Core.Services.Processing.SingleStream;

public class CategoryEventFilter(string category, bool allEvents, HashSet<string> events) : EventFilter(allEvents, false, events) {
	private readonly string _categoryStream = $"$ce-{category}";

	protected override bool DeletedNotificationPasses(string positionStreamId) => _categoryStream == positionStreamId;

	public override bool PassesSource(bool resolvedFromLinkTo, string positionStreamId, string eventType)
		=> resolvedFromLinkTo && _categoryStream == positionStreamId;

	public override string GetCategory(string positionStreamId)
		=> positionStreamId.StartsWith("$ce-")
			? positionStreamId["$ce-".Length..]
			: throw new ArgumentException($"'{positionStreamId}' is not a category stream", nameof(positionStreamId));

	public override string ToString() => $"Category: {category}, CategoryStream: {_categoryStream}";
}
