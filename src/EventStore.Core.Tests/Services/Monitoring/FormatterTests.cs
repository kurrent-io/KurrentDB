// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Common.Utils;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Monitoring;

[TestFixture]
public class FormatterTests {
	[Test]
	[TestCase(0L, ExpectedResult = "0B")]
	[TestCase(500L, ExpectedResult = "500B")]
	[TestCase(1023L, ExpectedResult = "1023B")]
	[TestCase(1024L, ExpectedResult = "1KiB")]
	[TestCase(2560L, ExpectedResult = "2.5KiB")]
	[TestCase(1048576L, ExpectedResult = "1MiB")]
	[TestCase(502792192L, ExpectedResult = "479.5MiB")]
	[TestCase(1073741824L, ExpectedResult = "1GiB")]
	[TestCase(79725330432L, ExpectedResult = "74.25GiB")]
	[TestCase(1099511627776L, ExpectedResult = "1TiB")]
	[TestCase(1125899906842624L, ExpectedResult = "1024TiB")]
	[TestCase(long.MaxValue, ExpectedResult = "8388608TiB")]
	[TestCase(-1L, ExpectedResult = "-1B")]
	[TestCase(-1023L, ExpectedResult = "-1023B")]
	[TestCase(-1024, ExpectedResult = "-1KiB")]
	[TestCase(-1048576L, ExpectedResult = "-1MiB")]
	public string test_size_multiple_cases_long(long bytes) {
		return bytes.ToFriendlySizeString();
	}

	[Test]
	[TestCase(0UL, ExpectedResult = "0B")]
	[TestCase(500UL, ExpectedResult = "500B")]
	[TestCase(1023UL, ExpectedResult = "1023B")]
	[TestCase(1024UL, ExpectedResult = "1KiB")]
	[TestCase(2560UL, ExpectedResult = "2.5KiB")]
	[TestCase(1048576UL, ExpectedResult = "1MiB")]
	[TestCase(502792192UL, ExpectedResult = "479.5MiB")]
	[TestCase(1073741824UL, ExpectedResult = "1GiB")]
	[TestCase(79725330432UL, ExpectedResult = "74.25GiB")]
	[TestCase(1099511627776UL, ExpectedResult = "1TiB")]
	[TestCase(1125899906842624UL, ExpectedResult = "1024TiB")]
	[TestCase(ulong.MaxValue, ExpectedResult = "more than long.MaxValue")] //16777215TiB
	public string test_size_multiple_cases_ulong(ulong bytes) {
		return bytes.ToFriendlySizeString();
	}


	[Test]
	[TestCase(0D, ExpectedResult = "0B/s")]
	[TestCase(500L, ExpectedResult = "500B/s")]
	[TestCase(1023L, ExpectedResult = "1023B/s")]
	[TestCase(1024L, ExpectedResult = "1KiB/s")]
	[TestCase(2560L, ExpectedResult = "2.5KiB/s")]
	[TestCase(1048576L, ExpectedResult = "1MiB/s")]
	[TestCase(502792192L, ExpectedResult = "479.5MiB/s")]
	[TestCase(1073741824L, ExpectedResult = "1GiB/s")]
	[TestCase(79725330432L, ExpectedResult = "74.25GiB/s")]
	[TestCase(-1L, ExpectedResult = "-1B/s")]
	[TestCase(-1023L, ExpectedResult = "-1023B/s")]
	[TestCase(-1024, ExpectedResult = "-1KiB/s")]
	[TestCase(-1048576L, ExpectedResult = "-1MiB/s")]
	public string test_speed_multiple_cases(double speed) {
		return speed.ToFriendlySpeedString();
	}


	[Test]
	[TestCase(0L, ExpectedResult = "0")]
	[TestCase(500L, ExpectedResult = "500")]
	[TestCase(1023L, ExpectedResult = "1023")]
	[TestCase(1024L, ExpectedResult = "1K")]
	[TestCase(2560L, ExpectedResult = "2.5K")]
	[TestCase(1048576L, ExpectedResult = "1M")]
	[TestCase(502792192L, ExpectedResult = "479.5M")]
	[TestCase(1073741824L, ExpectedResult = "1G")]
	[TestCase(79725330432L, ExpectedResult = "74.25G")]
	[TestCase(1099511627776L, ExpectedResult = "1T")]
	[TestCase(1125899906842624L, ExpectedResult = "1024T")]
	[TestCase(long.MaxValue, ExpectedResult = "8388608T")]
	[TestCase(-1L, ExpectedResult = "-1")]
	[TestCase(-1023L, ExpectedResult = "-1023")]
	[TestCase(-1024, ExpectedResult = "-1K")]
	[TestCase(-1048576L, ExpectedResult = "-1M")]
	public string test_number_multiple_cases_long(long number) {
		return number.ToFriendlyNumberString();
	}

	[Test]
	[TestCase(0UL, ExpectedResult = "0")]
	[TestCase(500UL, ExpectedResult = "500")]
	[TestCase(1023UL, ExpectedResult = "1023")]
	[TestCase(1024UL, ExpectedResult = "1K")]
	[TestCase(2560UL, ExpectedResult = "2.5K")]
	[TestCase(1048576UL, ExpectedResult = "1M")]
	[TestCase(502792192UL, ExpectedResult = "479.5M")]
	[TestCase(1073741824UL, ExpectedResult = "1G")]
	[TestCase(79725330432UL, ExpectedResult = "74.25G")]
	[TestCase(1099511627776UL, ExpectedResult = "1T")]
	[TestCase(1125899906842624UL, ExpectedResult = "1024T")]
	[TestCase(ulong.MaxValue, ExpectedResult = "more than long.MaxValue")] //16777215TiB
	public string test_number_multiple_cases_ulong(ulong number) {
		return number.ToFriendlyNumberString();
	}
}
