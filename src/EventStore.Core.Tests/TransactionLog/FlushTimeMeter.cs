// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using EventStore.Common.Utils;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture]
public class FlushTimeMeter : SpecificationWithFile {
	[Test, Ignore("long running")]
	public void Test() {
		var rnd = new Random();
		var sw = Stopwatch.StartNew();
		var gw = Stopwatch.StartNew();
		using (var fs = new FileStream(Filename, FileMode.OpenOrCreate)) {
			const int iter = 1000;
			for (int bytes = 100; bytes < 1000000; bytes *= 2) {
				var arr = new byte[bytes];
				rnd.NextBytes(arr);

				TimeSpan min = TimeSpan.FromHours(1);
				TimeSpan max = TimeSpan.Zero;

				gw.Restart();
				for (int i = 0; i < iter; ++i) {
					fs.Write(arr, 0, arr.Length);

					sw.Restart();
					fs.FlushToDisk();
					var elapsed = sw.Elapsed;

					min = elapsed < min ? elapsed : min;
					max = elapsed > max ? elapsed : max;
				}

				gw.Stop();

				Console.WriteLine("{0} bytes, Min: {1}, Max: {2}, Avg: {3}",
					bytes,
					min,
					max,
					TimeSpan.FromTicks(gw.Elapsed.Ticks / iter));
			}
		}
	}
}
