// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace KurrentDB.TestClient.Commands;

internal class RequestMonitor {
	private ConcurrentQueue<int> _measurements = new ConcurrentQueue<int>();
	private ConcurrentDictionary<Guid, Operation> _operations = new ConcurrentDictionary<Guid, Operation>();
	Stopwatch _watch = new Stopwatch();

	public RequestMonitor() {
		_watch.Start();
	}

	public void StartOperation(Guid id) {
		var record = new Operation();
		record.Start = _watch.ElapsedTicks;
		_operations.AddOrUpdate(id, record, (q, val) => record);
	}

	public void EndOperation(Guid id) {
		Operation record;
		if (_operations.TryRemove(id, out record)) {
			var current = _watch.ElapsedTicks;
			var time = current - record.Start;
			var ms = time / TimeSpan.TicksPerMillisecond;
			_measurements.Enqueue((int)ms);
		} else {
			Console.Write("x");
		}
	}

	public void PrintRawMeasurementDetails() {
		foreach (var i in _measurements) {
			Console.Write(i + ",");
		}
	}

	public void GetMeasurementDetails() {
		var items = _measurements.ToArray();
		Array.Sort(items);
		Console.WriteLine("fastest: " + items[0]);
		Console.WriteLine("quintiles");
		for (int i = 20; i <= 100; i += 20) {
			Console.WriteLine(i + "% : " + items[GetPercentile((decimal)i - 20, items.Length)] + "-" +
			                  items[GetPercentile((decimal)i, items.Length)]);
		}

		Console.WriteLine("90% : " + items[GetPercentile(90m, items.Length)]);
		Console.WriteLine("95% : " + items[GetPercentile(95m, items.Length)]);
		Console.WriteLine("98% : " + items[GetPercentile(98m, items.Length)]);
		Console.WriteLine("99% : " + items[GetPercentile(99m, items.Length)]);
		Console.WriteLine("99.5% : " + items[GetPercentile(99.5m, items.Length)]);
		Console.WriteLine("99.9% : " + items[GetPercentile(99.9m, items.Length)]);
		Console.WriteLine("99.99% : " + items[GetPercentile(99.99m, items.Length)]);
		Console.WriteLine("99.999% : " + items[GetPercentile(99.999m, items.Length)]);
		Console.WriteLine("99.9999% : " + items[GetPercentile(99.9999m, items.Length)]);
		Console.WriteLine("99.99999% : " + items[GetPercentile(99.99999m, items.Length)]);
		Console.WriteLine("Highest : " + items[items.Length - 1]);
	}

	private int GetPercentile(decimal percentile, int size) {
		decimal percent = 0;
		percent = percentile / 100m;
		var ret = (int)(percent * size);
		if (ret == size) ret -= 1;
		return ret;
	}

	struct Operation {
		public long Start;
	}
}
