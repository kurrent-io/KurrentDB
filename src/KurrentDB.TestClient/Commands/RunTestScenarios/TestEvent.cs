// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.ClientAPI;
using EventStore.Common.Utils;

namespace KurrentDB.TestClient.Commands.RunTestScenarios;

internal class TestEvent {
	public static EventData NewTestEvent(int index) {
		var subIndex = (index % 50);
		var type = "TestEvent-" + subIndex.ToString();
		var body = new string('#', 1 + subIndex * subIndex);
		var encodedData = Helper.UTF8NoBom.GetBytes(string.Format("{0}-{1}-{2}", index, body.Length, body));

		return new EventData(Guid.NewGuid(), type, false, encodedData, new byte[0]);
	}

	public static void VerifyIfMatched(RecordedEvent evnt) {
		if (evnt.EventType.StartsWith("TestEvent")) {
			var data = EventStore.Common.Utils.Helper.UTF8NoBom.GetString(evnt.Data);
			var atoms = data.Split('-');
			if (atoms.Length != 3)
				throw new ApplicationException(string.Format("Invalid TestEvent object: currupted data format: {0}",
					RecordDetailsString(evnt)));

			var expectedLength = int.Parse(atoms[1]);
			if (expectedLength != atoms[2].Length)
				throw new ApplicationException(string.Format(
					"Invalid TestEvent object: not expected data length: {0}",
					RecordDetailsString(evnt)));

			if (new string('#', expectedLength) != atoms[2])
				throw new ApplicationException(string.Format("Invalid TestEvent object: currupted data: {0}",
					RecordDetailsString(evnt)));
		}
	}

	private static string RecordDetailsString(RecordedEvent evnt) {
		var data = EventStore.Common.Utils.Helper.UTF8NoBom.GetString(evnt.Data);
		return string.Format("[stream:{0}; eventNumber:{1}; type:{2}; data:{3}]",
			evnt.EventStreamId,
			evnt.EventNumber,
			evnt.EventType,
			data.Length > 12 ? (data.Substring(0, 12) + "...") : data);
	}
}
