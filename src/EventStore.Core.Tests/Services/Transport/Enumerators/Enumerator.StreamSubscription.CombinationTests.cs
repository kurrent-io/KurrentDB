// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Data;
using EventStore.Core.Services;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Core.Services.UserManagement;
using NUnit.Framework;
using ExpectedVersion = EventStore.ClientAPI.ExpectedVersion;

namespace EventStore.Core.Tests.Services.Transport.Enumerators;

[TestFixture]
public partial class EnumeratorTests {
	[TestFixtureSource(nameof(TestCases))]
	public class StreamSubscriptionCombinationTests : TestFixtureWithMiniNodeConnection {
		public record struct StreamProperties(int NumEvents = 0, TruncationInfo TruncationInfo = new(), bool IsHardDeleted = false, bool IsEphemeralStream = false);
		public record struct SubscriptionProperties(CheckpointType CheckpointType = CheckpointType.Start);
		public record struct LiveProperties(int NumEventsToAdd = 0, bool SoftDeleteStream = false, bool HardDeleteStream = false, bool RevokeAccessWithStreamAcl = false, bool RevokeAccessWithDefaultAcl = false, bool FallBehindThenCatchUp = false);
		public readonly record struct TestData(string TestCase, StreamProperties StreamProperties, SubscriptionProperties SubscriptionProperties, LiveProperties LiveProperties) {
			public override string ToString() {
				return TestCase;
			}
		}
		public static object[] CreateTestData(string testCase, StreamProperties streamProperties, SubscriptionProperties subscriptionProperties, LiveProperties liveProperties) {
			return new object[] {
				new TestData(testCase, streamProperties, subscriptionProperties, liveProperties)
			};
		}

		public record struct TruncationInfo(TruncationType TruncationType, long? TruncationParam = null);
		public enum TruncationType {
			None = 0,
			SoftDelete = 1,
			TruncateBefore = 2,
			MaxCount = 3,
			ExpiredMaxAge = 4
		}

		public enum CheckpointType {
			Start = 0,
			End = 1,
			AtZero = 2,
			OneBeforeLast = 3,
			AtLast = 4,
			OneAfterLast = 5
		}

		private const int NumEventsToFallBehind = 3 * 32;

		public static object[] TestCases = {
			// NON-EXISTENT STREAM
			CreateTestData(
				"subscribe to a stream that doesn't exist from start",
				new StreamProperties(0),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream that doesn't exist at event number zero",
				new StreamProperties(0),
				new SubscriptionProperties(CheckpointType.AtZero),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream that doesn't exist from end",
				new StreamProperties(0),
				new SubscriptionProperties(CheckpointType.End),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream that doesn't exist after last event number",
				new StreamProperties(0),
				new SubscriptionProperties(CheckpointType.OneAfterLast),
				new LiveProperties(3)
			),
			// at the moment, it's not possible to soft delete a stream that doesn't exist
			/*
			CreateTestData(
				"subscribe to a stream that doesn't exist from start then soft delete it",
				new StreamProperties(0),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(SoftDeleteStream: true)
			),
			*/
			CreateTestData(
				"subscribe to a stream that doesn't exist from start then hard delete it",
				new StreamProperties(0),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(HardDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a stream that doesn't exist from start then revoke access with stream acl",
				new StreamProperties(0),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to a stream that doesn't exist from start then revoke access with default acl",
				new StreamProperties(0),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
			CreateTestData(
				"subscribe to a stream that doesn't exist from start then fall behind and catch up",
				new StreamProperties(0),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(FallBehindThenCatchUp: true)
			),
			// EXISTING STREAM
			CreateTestData(
				"subscribe to a stream that exists from start",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream that exists from end",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.End),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream that exists at event number zero",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.AtZero),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream that exists at one before last event number",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.OneBeforeLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream that exists at last event number",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.AtLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream that exists at one after last event number",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.OneAfterLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream that exists from start then soft delete it",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(SoftDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a stream that exists from start then hard delete it",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(HardDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a stream that exists from start then revoke access with stream acl",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to a stream that exists from start then revoke access with default acl",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
			CreateTestData(
				"subscribe to a stream that exists from start then fall behind and catch up",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(FallBehindThenCatchUp: true)
			),
			// SOFT DELETED STREAM
			CreateTestData(
				"subscribe to a soft deleted stream from start",
				new StreamProperties(10, new TruncationInfo(TruncationType.SoftDelete)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a soft deleted stream from end",
				new StreamProperties(10, new TruncationInfo(TruncationType.SoftDelete)),
				new SubscriptionProperties(CheckpointType.End),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a soft deleted stream at event number zero",
				new StreamProperties(10, new TruncationInfo(TruncationType.SoftDelete)),
				new SubscriptionProperties(CheckpointType.AtZero),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a soft deleted stream at one before last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.SoftDelete)),
				new SubscriptionProperties(CheckpointType.OneBeforeLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a soft deleted stream at last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.SoftDelete)),
				new SubscriptionProperties(CheckpointType.AtLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a soft deleted stream at one after last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.SoftDelete)),
				new SubscriptionProperties(CheckpointType.OneAfterLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a soft deleted stream from start then soft delete it again",
				new StreamProperties(10, new TruncationInfo(TruncationType.SoftDelete)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(SoftDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a soft deleted stream from start then hard delete it",
				new StreamProperties(10, new TruncationInfo(TruncationType.SoftDelete)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(HardDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a soft deleted stream from start then revoke access with stream acl",
				new StreamProperties(10, new TruncationInfo(TruncationType.SoftDelete)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to a soft deleted stream from start then revoke access with default acl",
				new StreamProperties(10, new TruncationInfo(TruncationType.SoftDelete)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
			CreateTestData(
				"subscribe to a soft deleted stream from start then fall behind and catch up",
				new StreamProperties(10, new TruncationInfo(TruncationType.SoftDelete)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(FallBehindThenCatchUp: true)
			),
			// FULLY TRUNCATED STREAM
			CreateTestData(
				"subscribe to a fully truncated stream from start",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 10)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a fully truncated stream from end",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 10)),
				new SubscriptionProperties(CheckpointType.End),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a fully truncated stream at event number zero",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 10)),
				new SubscriptionProperties(CheckpointType.AtZero),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a fully truncated stream at one before last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 10)),
				new SubscriptionProperties(CheckpointType.OneBeforeLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a fully truncated stream at last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 10)),
				new SubscriptionProperties(CheckpointType.AtLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a fully truncated stream at one after last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 10)),
				new SubscriptionProperties(CheckpointType.OneAfterLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a fully truncated stream from start then soft delete it",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 10)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(SoftDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a fully truncated stream from start then hard delete it",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 10)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(HardDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a fully truncated stream from start then revoke access with stream acl",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 10)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to a fully truncated stream from start then revoke access with default acl",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 10)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
			CreateTestData(
				"subscribe to a fully truncated stream from start then fall behind and catch up",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 10)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(FallBehindThenCatchUp: true)
			),
			// PARTLY TRUNCATED STREAM
			CreateTestData(
				"subscribe to a partly truncated stream from start",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 8)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a partly truncated stream from end",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 8)),
				new SubscriptionProperties(CheckpointType.End),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a partly truncated stream at event number zero",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 8)),
				new SubscriptionProperties(CheckpointType.AtZero),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a partly truncated stream at one before last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 8)),
				new SubscriptionProperties(CheckpointType.OneBeforeLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a partly truncated stream at last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 8)),
				new SubscriptionProperties(CheckpointType.AtLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a partly truncated stream at one after last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 8)),
				new SubscriptionProperties(CheckpointType.OneAfterLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a partly truncated stream from start then soft delete it",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 8)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(SoftDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a partly truncated stream from start then hard delete it",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 8)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(HardDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a partly truncated stream from start then revoke access with stream acl",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 8)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to a partly truncated stream from start then revoke access with default acl",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 8)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
			CreateTestData(
				"subscribe to a partly truncated stream from start then fall behind and catch up",
				new StreamProperties(10, new TruncationInfo(TruncationType.TruncateBefore, 8)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(FallBehindThenCatchUp: true)
			),
			// MAX COUNT
			CreateTestData(
				"subscribe to a stream with max count from start",
				new StreamProperties(10, new TruncationInfo(TruncationType.MaxCount, 3)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with max count from end",
				new StreamProperties(10, new TruncationInfo(TruncationType.MaxCount, 3)),
				new SubscriptionProperties(CheckpointType.End),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with max count at event number zero",
				new StreamProperties(10, new TruncationInfo(TruncationType.MaxCount, 3)),
				new SubscriptionProperties(CheckpointType.AtZero),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with max count at one before last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.MaxCount, 3)),
				new SubscriptionProperties(CheckpointType.OneBeforeLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with max count at last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.MaxCount, 3)),
				new SubscriptionProperties(CheckpointType.AtLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with max count at one after last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.MaxCount, 3)),
				new SubscriptionProperties(CheckpointType.OneAfterLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with max count from start then soft delete it",
				new StreamProperties(10, new TruncationInfo(TruncationType.MaxCount, 3)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(SoftDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a stream with max count from start then hard delete it",
				new StreamProperties(10, new TruncationInfo(TruncationType.MaxCount, 3)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(HardDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a stream with max count from start then revoke access with stream acl",
				new StreamProperties(10, new TruncationInfo(TruncationType.MaxCount, 3)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to a stream with max count from start then revoke access with default acl",
				new StreamProperties(10, new TruncationInfo(TruncationType.MaxCount, 3)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
			CreateTestData(
				"subscribe to a stream with max count from start then fall behind and catch up",
				new StreamProperties(120, new TruncationInfo(TruncationType.MaxCount, 100)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(FallBehindThenCatchUp: true)
			),
			// EXPIRED MAX AGE (1 second)
			CreateTestData(
				"subscribe to a stream with expired max age from start",
				new StreamProperties(10, new TruncationInfo(TruncationType.ExpiredMaxAge)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with expired max age from end",
				new StreamProperties(10, new TruncationInfo(TruncationType.ExpiredMaxAge)),
				new SubscriptionProperties(CheckpointType.End),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with expired max age at event number zero",
				new StreamProperties(10, new TruncationInfo(TruncationType.ExpiredMaxAge)),
				new SubscriptionProperties(CheckpointType.AtZero),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with expired max age at one before last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.ExpiredMaxAge)),
				new SubscriptionProperties(CheckpointType.OneBeforeLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with expired max age at last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.ExpiredMaxAge)),
				new SubscriptionProperties(CheckpointType.AtLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with expired max age at one after last event number",
				new StreamProperties(10, new TruncationInfo(TruncationType.ExpiredMaxAge)),
				new SubscriptionProperties(CheckpointType.OneAfterLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a stream with expired max age from start then soft delete it",
				new StreamProperties(10, new TruncationInfo(TruncationType.ExpiredMaxAge)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(SoftDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a stream with expired max age from start then hard delete it",
				new StreamProperties(10, new TruncationInfo(TruncationType.ExpiredMaxAge)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(HardDeleteStream: true)
			),
			CreateTestData(
				"subscribe to a stream with expired max age from start then revoke access with stream acl",
				new StreamProperties(10, new TruncationInfo(TruncationType.ExpiredMaxAge)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to a stream with expired max age from start then revoke access with default acl",
				new StreamProperties(10, new TruncationInfo(TruncationType.ExpiredMaxAge)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
			CreateTestData(
				"subscribe to a stream with expired max age from start then fall behind and catch up",
				new StreamProperties(10, new TruncationInfo(TruncationType.ExpiredMaxAge)),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(FallBehindThenCatchUp: true)
			),
			// HARD DELETED STREAM
			CreateTestData(
				"subscribe to a hard deleted stream from start",
				new StreamProperties(10, IsHardDeleted: true),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a hard deleted stream from end",
				new StreamProperties(10, IsHardDeleted: true),
				new SubscriptionProperties(CheckpointType.End),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a hard deleted stream at event number zero",
				new StreamProperties(10, IsHardDeleted: true),
				new SubscriptionProperties(CheckpointType.AtZero),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a hard deleted stream at one before last event number",
				new StreamProperties(10, IsHardDeleted: true),
				new SubscriptionProperties(CheckpointType.OneBeforeLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a hard deleted stream at last event number",
				new StreamProperties(10, IsHardDeleted: true),
				new SubscriptionProperties(CheckpointType.AtLast),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to a hard deleted stream at one after last event number",
				new StreamProperties(10, IsHardDeleted: true),
				new SubscriptionProperties(CheckpointType.OneAfterLast),
				new LiveProperties(3)
			),
			// EPHEMERAL STREAM
			CreateTestData(
				"subscribe to an ephemeral stream that exists from start",
				new StreamProperties(IsEphemeralStream: true),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties()
			),
			CreateTestData(
				"subscribe to an ephemeral stream that exists from end",
				new StreamProperties(IsEphemeralStream: true),
				new SubscriptionProperties(CheckpointType.End),
				new LiveProperties()
			),
			CreateTestData(
				"subscribe to an ephemeral stream that exists at event number zero",
				new StreamProperties(IsEphemeralStream: true),
				new SubscriptionProperties(CheckpointType.AtZero),
				new LiveProperties()
			),
			CreateTestData(
				"subscribe to an ephemeral stream that exists at one before last event number",
				new StreamProperties(IsEphemeralStream: true),
				new SubscriptionProperties(CheckpointType.OneBeforeLast),
				new LiveProperties()
			),
			CreateTestData(
				"subscribe to an ephemeral stream that exists at last event number",
				new StreamProperties(IsEphemeralStream: true),
				new SubscriptionProperties(CheckpointType.AtLast),
				new LiveProperties()
			),
			CreateTestData(
				"subscribe to an ephemeral stream that exists at one after last event number",
				new StreamProperties(IsEphemeralStream: true),
				new SubscriptionProperties(CheckpointType.OneAfterLast),
				new LiveProperties()
			),
			CreateTestData(
				"subscribe to an ephemeral stream that exists from start then revoke access with stream acl",
				new StreamProperties(IsEphemeralStream: true),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to an ephemeral stream that exists from start then revoke access with default acl",
				new StreamProperties(IsEphemeralStream: true),
				new SubscriptionProperties(CheckpointType.Start),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
		};

		private readonly TestData _testData;
		private StreamProperties StreamProps => _testData.StreamProperties;
		private SubscriptionProperties SubscriptionProps => _testData.SubscriptionProperties;
		private LiveProperties LiveProps => _testData.LiveProperties;

		private readonly string _stream;
		private long _nextEventNumber;
		private long _ephemeralStreamLastEventNumber;

		private bool HardDeleted => StreamProps.IsHardDeleted;
		private long FirstEventNumber {
			get {
				if (StreamProps.IsEphemeralStream)
					return _ephemeralStreamLastEventNumber; // the $mem-node-state ephemeral stream keeps only the last event

				return StreamProps.TruncationInfo.TruncationType switch {
					TruncationType.None => 0L,
					TruncationType.SoftDelete => StreamProps.NumEvents,
					TruncationType.TruncateBefore => StreamProps.TruncationInfo.TruncationParam!.Value,
					TruncationType.MaxCount => Math.Max(0L, StreamProps.NumEvents - StreamProps.TruncationInfo.TruncationParam!.Value),
					TruncationType.ExpiredMaxAge => StreamProps.NumEvents,
					_ => throw new ArgumentOutOfRangeException()
				};
			}
		}

		private long LastEventNumber {
			get {
				if (StreamProps.IsEphemeralStream)
					return _ephemeralStreamLastEventNumber;

				return StreamProps.NumEvents - 1;
			}
		}

		public StreamSubscriptionCombinationTests(TestData testData) {
			_testData = testData;
			_stream = StreamProps.IsEphemeralStream ? "$mem-node-state" : $"stream-{Guid.NewGuid()}";
			_nextEventNumber = CalculateNextEventNumberFromCheckpoint();
		}

		private async Task WriteExistingEvents() {
			if (StreamProps is { IsEphemeralStream: true, NumEvents: > 0 })
				throw new Exception("Ephemeral streams cannot be written to.");

			var numEvents = StreamProps.NumEvents;
			for (int i = 0; i < numEvents; i++)
				await WriteEvent();
		}

		private async Task WriteEvent(string stream, string eventType, string data, string metadata) {
			data ??= string.Empty;
			metadata ??= string.Empty;
			var eventData = new EventData(Guid.NewGuid(), eventType, true, Encoding.UTF8.GetBytes(data), Encoding.UTF8.GetBytes(metadata));
			await NodeConnection.AppendToStreamAsync(stream, ExpectedVersion.Any, eventData);
		}
		private Task WriteEvent() => WriteEvent(_stream, "type", "{}", null);
		private async Task SoftDelete() => await NodeConnection.DeleteStreamAsync(_stream, Data.ExpectedVersion.Any, hardDelete: false);
		private async Task Tombstone() => await NodeConnection.DeleteStreamAsync(_stream, Data.ExpectedVersion.Any, hardDelete: true);
		private async Task RevokeAccessWithStreamAcl() => await WriteEvent(SystemStreams.MetastreamOf(_stream), "$metadata", @"{ ""$acl"": { ""$r"": [] } }", null);
		private async Task RevokeAccessWithDefaultAcl() => await WriteEvent(SystemStreams.SettingsStream, "update-default-acl", @"{ ""$userStreamAcl"" : { ""$r"" : [] } }", null);

		private Task WriteMetadata(string metadata) {
			var metaStream = SystemStreams.MetastreamOf(_stream);
			const string metaEventType = "$metadata";
			return WriteEvent(metaStream, metaEventType, metadata, null);
		}

		private Task Truncate(long tb) => WriteMetadata(@$"{{""$tb"":{tb}}}");
		private Task MaxCount(long maxCount) => WriteMetadata(@$"{{""$maxCount"":{maxCount}}}");
		private async Task ExpiredMaxAge() {
			// a max age of zero doesn't do anything, so we're forced to introduce a delay of 1 second. Make it 2 to be sure.
			await WriteMetadata(@"{""$maxAge"": 1 }"); // seconds
			await Task.Delay(TimeSpan.FromMilliseconds(2000));
		}

		private Task ApplyTruncation() {
			if (StreamProps.IsEphemeralStream && StreamProps.TruncationInfo.TruncationType != TruncationType.None)
				throw new Exception("Ephemeral streams cannot be truncated.");

			var truncationParam = StreamProps.TruncationInfo.TruncationParam;
			switch (StreamProps.TruncationInfo.TruncationType) {
				case TruncationType.None:
					break;
				case TruncationType.SoftDelete:
					return SoftDelete();
				case TruncationType.TruncateBefore:
					return Truncate(truncationParam!.Value);
				case TruncationType.MaxCount:
					return MaxCount(truncationParam!.Value);
				case TruncationType.ExpiredMaxAge:
					return ExpiredMaxAge();
				default:
					throw new ArgumentOutOfRangeException();
			}

			return Task.CompletedTask;
		}

		private Task ApplyTombstone() {
			if (StreamProps is { IsEphemeralStream: true, IsHardDeleted: true })
				throw new Exception("Ephemeral streams cannot be hard deleted.");

			return StreamProps.IsHardDeleted ? Tombstone() : Task.CompletedTask;
		}

		private async Task<(int numEventsAdded, bool softDeleted, bool hardDeleted, bool accessRevoked, bool fallBehindThenCatchup)> ApplyLiveProperties() {
			var numEventsAdded = 0;
			var softDeleted = false;
			var hardDeleted = false;
			var accessRevoked = false;
			var shouldFallBehindThenCatchup = false;

			if (LiveProps.NumEventsToAdd > 0) {
				if (StreamProps.IsEphemeralStream)
					throw new Exception("Ephemeral streams cannot be written to.");

				numEventsAdded = LiveProps.NumEventsToAdd;
				for (var i = 0; i < numEventsAdded; i++)
					await WriteEvent();
			} else if (LiveProps.SoftDeleteStream) {
				if (StreamProps.IsEphemeralStream)
					throw new Exception("Ephemeral streams cannot be soft deleted.");

				softDeleted = true;
				await SoftDelete();
			} else if (LiveProps.HardDeleteStream) {
				if (StreamProps.IsEphemeralStream)
					throw new Exception("Ephemeral streams cannot be hard deleted.");

				hardDeleted = true;
				await Tombstone();
			} else if (LiveProps.RevokeAccessWithStreamAcl) {
				accessRevoked = true;
				await RevokeAccessWithStreamAcl();
			} else if (LiveProps.RevokeAccessWithDefaultAcl) {
				accessRevoked = true;
				await RevokeAccessWithDefaultAcl();
			} else if (LiveProps.FallBehindThenCatchUp) {
				numEventsAdded = NumEventsToFallBehind;
				for (var i = 0; i < NumEventsToFallBehind; i++)
					await WriteEvent();

				shouldFallBehindThenCatchup = true;
			}

			return (numEventsAdded, softDeleted, hardDeleted, accessRevoked, shouldFallBehindThenCatchup);
		}

		private async Task SetUpForEphemeralStream() {
			if (!StreamProps.IsEphemeralStream)
				return;

			var readResult = await NodeConnection.ReadStreamEventsBackwardAsync(_stream, -1, 1, resolveLinkTos: false);

			_ephemeralStreamLastEventNumber = readResult.LastEventNumber;
			_nextEventNumber = CalculateNextEventNumberFromCheckpoint();
		}

		private long CalculateNextEventNumberFromCheckpoint() {
			return SubscriptionProps.CheckpointType switch {
				CheckpointType.Start => FirstEventNumber,
				CheckpointType.End => LastEventNumber + 1,
				CheckpointType.AtZero => Math.Max(1, FirstEventNumber),
				CheckpointType.OneBeforeLast => Math.Max(LastEventNumber, FirstEventNumber),
				CheckpointType.AtLast => LastEventNumber + 1,
				CheckpointType.OneAfterLast => LastEventNumber + 2,
				_ => throw new ArgumentOutOfRangeException()
			};
		}

		private EnumeratorWrapper Subscribe() {
			StreamRevision? checkpoint = SubscriptionProps.CheckpointType switch {
				CheckpointType.Start => null,
				CheckpointType.End => StreamRevision.End,
				CheckpointType.AtZero => StreamRevision.FromInt64(0),
				CheckpointType.OneAfterLast => StreamRevision.FromInt64(LastEventNumber + 1),
				CheckpointType.AtLast => StreamRevision.FromInt64(LastEventNumber),
				CheckpointType.OneBeforeLast => StreamRevision.FromInt64(LastEventNumber - 1),
				_ => throw new ArgumentOutOfRangeException()
			};

			return CreateStreamSubscription<string>(Node.Node.MainQueue, _stream, checkpoint, SystemAccounts.Anonymous);
		}

		private static async Task<long> ReadExpectedEvents(EnumeratorWrapper sub, long nextEventNumber, long lastEventNumber, bool shouldFallBehindThenCatchUp = false) {
			var fellBehind = false;
			var caughtUp = false;

			var numResponsesExpected = lastEventNumber - nextEventNumber + 1;
			if (shouldFallBehindThenCatchUp)
				numResponsesExpected += 2;

			while (--numResponsesExpected >= 0) {
				var response = await sub.GetNext();
				switch (response) {
					case Event evt:
						Assert.AreEqual(nextEventNumber++, evt.EventNumber);
						break;
					case FellBehind:
						if (!shouldFallBehindThenCatchUp)
							Assert.Fail("Subscription fell behind.");

						fellBehind = true;
						break;
					case CaughtUp:
						if (!fellBehind)
							Assert.Fail("Subscription caught up before falling behind");

						if (!shouldFallBehindThenCatchUp)
							Assert.Fail("Subscription fell behind then caught up.");

						caughtUp = true;
						break;
					default:
						Assert.Fail($"Unexpected response: {response}");
						break ;
				}
			}

			if (shouldFallBehindThenCatchUp) {
				if (!fellBehind)
					Assert.Fail("Subscription did not fall behind.");

				if (!caughtUp)
					Assert.Fail("Subscription fell behind but did not catch up.");
			}

			return nextEventNumber;
		}

		[SetUp]
		public async Task SetUp() {
			await WriteExistingEvents();
			await ApplyTruncation();
			await ApplyTombstone();
			await SetUpForEphemeralStream();
		}

		[Test]
		public async Task enumeration_is_correct() {
			var sub = Subscribe();

			if (HardDeleted) {
				Assert.ThrowsAsync<ReadResponseException.StreamDeleted>(async () => await sub.GetNext());
				return;
			}

			var current = await sub.GetNext();
			Assert.True(
				current is SubscriptionConfirmation,
				"Case \"{0}\": Expected SubscriptionConfirmation but was {1}",
				_testData.TestCase,
				current.GetType().FullName);

			_nextEventNumber = await ReadExpectedEvents(sub, _nextEventNumber, LastEventNumber);

			current = await sub.GetNext();
			Assert.True(
				current is CaughtUp,
				"Case \"{0}\": Expected CaughtUp but was {1}",
				_testData.TestCase,
				current.GetType().FullName);

			var (numEventsAdded, softDeleted, hardDeleted, accessRevoked, shouldFallBehindThenCatchup) = await ApplyLiveProperties();

			_nextEventNumber = await ReadExpectedEvents(sub, _nextEventNumber, LastEventNumber + numEventsAdded, shouldFallBehindThenCatchup);

			if (softDeleted) {
				Assert.ThrowsAsync<TimeoutException>(async () => await sub.GetNext().WithTimeout(timeoutMs: 500));
			} else if (hardDeleted) {
				await ReadExpectedEvents(sub, EventNumber.DeletedStream, EventNumber.DeletedStream);
				Assert.ThrowsAsync<ReadResponseException.StreamDeleted>(async () => await sub.GetNext());
			} else if (accessRevoked) {
				Assert.ThrowsAsync<ReadResponseException.AccessDenied>(async () => await sub.GetNext());
			}
		}
	}
}
