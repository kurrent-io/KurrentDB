// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace Checker;

/// <summary>
/// What one writer recorded as acknowledged. Written by the writer, read here.
/// </summary>
public sealed record Ledger(
	string WriterId,
	long Acked,
	long Failed,
	SortedDictionary<string, long> HighWaterMarks);

public static class Ledgers {
	private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

	public static IReadOnlyList<Ledger> Read(string directory) {
		var ledgers = new List<Ledger>();

		if (!Directory.Exists(directory)) {
			Console.WriteLine($"[ledger] {directory} does not exist");
			return ledgers;
		}

		foreach (var file in Directory.EnumerateFiles(directory, "writer-*.json").OrderBy(f => f)) {
			try {
				var ledger = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(file), Json);
				if (ledger is not null)
					ledgers.Add(ledger);
			} catch (Exception ex) {
				Console.WriteLine($"[ledger] could not read {file}: {ex.Message}");
			}
		}

		return ledgers;
	}

	/// <summary>
	/// Writers flush their final ledger and then drop a .done marker, so waiting for the markers
	/// avoids verifying against a ledger that is still being written.
	/// </summary>
	public static async Task WaitForWritersAsync(
		string directory, int expected, TimeSpan timeout, CancellationToken token) {

		if (expected <= 0)
			return;

		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline) {
			var done = Directory.Exists(directory)
				? Directory.EnumerateFiles(directory, "writer-*.done").Count()
				: 0;

			if (done >= expected) {
				Console.WriteLine($"[ledger] all {expected} writer(s) finished");
				return;
			}

			await Task.Delay(TimeSpan.FromSeconds(1), token);
		}

		Console.WriteLine($"[ledger] timed out waiting for writers to finish; verifying what is there");
	}
}
