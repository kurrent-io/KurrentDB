// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Threading.Tasks;
using EventStore.Common.Options;
using EventStore.Core.Exceptions;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexVAny;

[TestFixture]
public class when_opening_ptable_without_right_flag_in_header : SpecificationWithFile {
	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();
		using (var stream = File.OpenWrite(Filename)) {
			var bytes = new byte[128];
			bytes[0] = 0x27;
			stream.Write(bytes, 0, bytes.Length);
		}
	}

	[Test]
	public void the_invalid_file_exception_is_thrown() {
		var exc = Assert.Throws<CorruptIndexException>(() => PTable.FromFile(Filename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, 16, false));
		Assert.IsInstanceOf<InvalidFileException>(exc.InnerException);
	}
}
