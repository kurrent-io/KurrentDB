// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests;

public static class AssertEx {
	public static void AreEqual(IQuerySources expected, IQuerySources actual) {
		Assert.AreEqual(expected.AllEvents, actual.AllEvents, $"Expected {nameof(expected.AllEvents)} to be {expected.AllEvents} but was {actual.AllEvents}");
		Assert.AreEqual(expected.AllStreams, actual.AllStreams, $"Expected {nameof(expected.AllStreams)} to be {expected.AllStreams} but was {actual.AllStreams}");
		Assert.AreEqual(expected.ByCustomPartitions, actual.ByCustomPartitions,$"Expected {nameof(expected.ByCustomPartitions)} to be {expected.ByCustomPartitions} but was {actual.ByCustomPartitions}");
		Assert.AreEqual(expected.ByStreams, actual.ByStreams, $"Expected {nameof(expected.ByStreams)} to be {expected.ByStreams} but was {actual.ByStreams}");
		Assert.AreEqual(expected.DefinesFold, actual.DefinesFold, $"Expected {nameof(expected.DefinesFold)} to be {expected.DefinesFold} but was {actual.DefinesFold}");
		Assert.AreEqual(expected.DefinesStateTransform, actual.DefinesStateTransform, $"Expected {nameof(expected.DefinesStateTransform)} to be {expected.DefinesStateTransform} but was {actual.DefinesStateTransform}");

		Assert.AreEqual(expected.IncludeLinksOption, actual.IncludeLinksOption, $"Expected {nameof(expected.IncludeLinksOption)} to be {expected.IncludeLinksOption} but was {actual.IncludeLinksOption}");
		Assert.AreEqual(expected.IsBiState, actual.IsBiState, $"Expected {nameof(expected.IsBiState)} to be {expected.IsBiState} but was {actual.IsBiState}");
		Assert.AreEqual(expected.LimitingCommitPosition, actual.LimitingCommitPosition, $"Expected {nameof(expected.LimitingCommitPosition)} to be {expected.LimitingCommitPosition} but was {actual.LimitingCommitPosition}");
		Assert.AreEqual(expected.PartitionResultStreamNamePatternOption, actual.PartitionResultStreamNamePatternOption, $"Expected {nameof(expected.PartitionResultStreamNamePatternOption)} to be {expected.PartitionResultStreamNamePatternOption} but was {actual.PartitionResultStreamNamePatternOption}");
		Assert.AreEqual(expected.ProcessingLagOption, actual.ProcessingLagOption, $"Expected {nameof(expected.ProcessingLagOption)} to be {expected.ProcessingLagOption} but was {actual.ProcessingLagOption}");
		Assert.AreEqual(expected.ProducesResults, actual.ProducesResults, $"Expected {nameof(expected.ProducesResults)} to be {expected.ProducesResults} but was {actual.ProducesResults}");
		Assert.AreEqual(expected.ReorderEventsOption, actual.ReorderEventsOption, $"Expected {nameof(expected.ReorderEventsOption)} to be {expected.ReorderEventsOption} but was {actual.ReorderEventsOption}");
		Assert.AreEqual(expected.ResultStreamNameOption, actual.ResultStreamNameOption, $"Expected {nameof(expected.ResultStreamNameOption)} to be {expected.ResultStreamNameOption} but was {actual.ResultStreamNameOption}");
		Assert.AreEqual(expected.HandlesDeletedNotifications, actual.HandlesDeletedNotifications, $"Expected {nameof(expected.HandlesDeletedNotifications)} to be {expected.HandlesDeletedNotifications} but was {actual.HandlesDeletedNotifications}");
		AssertWithName(nameof(expected.Categories),expected.Categories, actual.Categories);
		AssertWithName(nameof(expected.Events),expected.Events, actual.Events);
		AssertWithName(nameof(expected.Streams),expected.Streams, actual.Streams);

		static void AssertWithName(string name, string[] expected, string[] actual) {
			try {
				Assert.AreEqual(expected, actual);
			} catch (Exception ex) {
				var msg = name + ex.Message;
				throw new Exception(msg);
			}
		}
	}
}
