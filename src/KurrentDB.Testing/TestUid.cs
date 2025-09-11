// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;

namespace KurrentDB.Testing;

/// <summary>
/// Generates short unique identifiers for tests to be able to
/// correlate log, tracing or other info to a specific test run.
/// <remarks>
/// The generated ID is a hexadecimal string derived from a folded GUID,
/// ensuring uniqueness while keeping the identifier concise.
/// </remarks>
/// </summary>
[PublicAPI]
public static class TestUid {
	/// <summary>
	/// Represents an empty TestUid
	/// </summary>
	public const string Empty = "00000000";

	/// <summary>
	/// Generate a new random TestUid
	/// </summary>
	public static unsafe string New() {
		var guid = Guid.NewGuid();
		Span<byte> bytes = stackalloc byte[16];
		guid.TryWriteBytes(bytes);

		ulong folded = MemoryMarshal.Read<ulong>(bytes) ^
		               MemoryMarshal.Read<ulong>(bytes[8..]);

		return folded.ToString("X8");
	}

	/// <summary>
	/// Check if a TestId is empty
	/// </summary>
	public static bool IsEmpty(string testId) => testId == Empty;

	/// <summary>
	/// Validates if a given TestId is a valid 8-character hexadecimal string.
	/// </summary>
	public static bool IsValid(ReadOnlySpan<char> testId) {
		if (testId.Length != 8)
			return false;

		foreach (char c in testId)
			if (!Uri.IsHexDigit(c)) return false;

		return true;
	}
}

public static class TestContextExtensions {
	public const string TestUidKey = $"${nameof(TestUid)}";

	/// <summary>
	/// Assigns a new unique test id to the TestContext's ObjectBag.
	/// </summary>
	public static string AssignTestUid(this TestContext ctx) {
		var testUid = TestUid.New();
		ctx.ObjectBag.Add(TestUidKey,  testUid);
		return testUid;
	}

	/// <summary>
	/// Extracts the test unique id from the TestContext's ObjectBag.
	/// </summary>
	public static string GetTestUid(this TestContext? ctx) {
		return ctx is null || !ctx.ObjectBag.TryGetValue(TestUidKey, out var val) || val is not string uid || TestUid.IsEmpty(uid)
			? throw new KeyNotFoundException("Failed to find TestUid in the TestContext ObjectBag!")
			: TestUid.IsValid(uid) ? uid : throw new FormatException($"Invalid TestUid '{uid}' format!");
	}
}
