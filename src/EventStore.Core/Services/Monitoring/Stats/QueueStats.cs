// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;

namespace EventStore.Core.Services.Monitoring.Stats;

public class QueueStats {
	///<summary>
	///Queue name
	///</summary>
	public readonly string Name;

	///<summary>
	///Group queue is a member of
	///</summary>
	public readonly string GroupName;

	///<summary>
	///Number of items in the queue
	///</summary>
	public readonly int Length;

	///<summary>
	///The highest number of items in the queue
	///</summary>
	public readonly long LengthLifetimePeak;

	///<summary>
	///The highest number of items in the queue within the past 100ms
	///</summary>
	public readonly long LengthCurrentTryPeak;

	///<summary>
	///Time elapsed processing the current item
	///</summary>
	public readonly TimeSpan? CurrentItemProcessingTime;

	///<summary>
	///Time elapsed since queue went idle
	///</summary>
	public readonly TimeSpan? CurrentIdleTime;

	///<summary>
	///The total number of items processed by the queue.
	///</summary>
	public readonly long TotalItemsProcessed;

	///<summary>
	///The average number of items processed per second by the queue.
	///</summary>
	public readonly int AvgItemsPerSecond;

	///<summary>
	///Average number of items processed per second
	///</summary>
	public readonly double AvgProcessingTime;

	///<summary>
	///Percentage of time queue spent idle
	///</summary>
	public readonly double IdleTimePercent;

	///<summary>
	///Last message type processed
	///</summary>
	public readonly Type LastProcessedMessageType;

	///<summary>
	///Current message type queue is processing
	///</summary>
	public readonly Type InProgressMessageType;

	public QueueStats(string name,
		string groupName,
		int length,
		int avgItemsPerSecond,
		double avgProcessingTime,
		double idleTimePercent,
		TimeSpan? currentItemProcessingTime,
		TimeSpan? currentIdleTime,
		long totalItemsProcessed,
		long lengthCurrentTryPeak,
		long lengthLifetimePeak,
		Type lastProcessedMessageType,
		Type inProgressMessageType) {
		Name = name;
		GroupName = groupName;
		Length = length;
		AvgItemsPerSecond = avgItemsPerSecond;
		AvgProcessingTime = avgProcessingTime;
		IdleTimePercent = idleTimePercent;
		CurrentItemProcessingTime = currentItemProcessingTime;
		CurrentIdleTime = currentIdleTime;
		TotalItemsProcessed = totalItemsProcessed;
		LengthCurrentTryPeak = lengthCurrentTryPeak;

		LengthLifetimePeak = lengthLifetimePeak;

		LastProcessedMessageType = lastProcessedMessageType;
		InProgressMessageType = inProgressMessageType;
	}

	public override string ToString() {
		var str = string.Format("{0,-22} L: {1,-5}      Avg: {5,-5}i/s    AvgProcTime: {6:0.0}ms\n"
		                        + "      Idle %:{7,-5:00.0}  Peak: {2,-5}  MaxPeak: {3,-7}  TotalProcessed: {4,-7}\n"
		                        + "      Processing: {8}, Last: {9}",
			Name,
			Length,
			LengthCurrentTryPeak,
			LengthLifetimePeak,
			TotalItemsProcessed,
			AvgItemsPerSecond,
			AvgProcessingTime,
			IdleTimePercent,
			InProgressMessageType == null ? "<none>" : InProgressMessageType.Name,
			LastProcessedMessageType == null ? "<none>" : LastProcessedMessageType.Name);
		return str;
	}
}
