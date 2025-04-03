// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using NUnit.Framework;

namespace EventStore.Core.Tests;

[TestFixture]
public class mono_uritemplate_bug {
	[Test]
	public void when_validating_a_uri_template_with_url_encoded_chars() {
		var template = new UriTemplate("/streams/$all?embed={embed}");
		var uri = new Uri("http://127.0.0.1/streams/$all");
		var baseaddress = new Uri("http://127.0.0.1");
		Assert.IsTrue(template.Match(baseaddress, uri) != null);
	}
}


[TestFixture, Ignore("Known bug in Mono, waiting for fix.")]
public class mono_filestream_bug {
	[Test]
	public void show_time() {
		const int pos = 1;
		const int bufferSize = 128;

		var filename = Path.GetTempFileName();
		File.WriteAllBytes(filename, new byte[pos + 1]); // init file with zeros

		var bytes = new byte[bufferSize + 1 /* THIS IS WHAT MAKES A BIG DIFFERENCE */];
		new Random().NextBytes(bytes);

		using (var file = new FileStream(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.Read,
			bufferSize, FileOptions.SequentialScan)) {
			file.Read(new byte[pos], 0, pos); // THIS READ IS CRITICAL, WITHOUT IT EVERYTHING WORKS
			Assert.AreEqual(pos,
				file.Position); // !!! here it says position is correct, but writes at different position !!!
			// file.Position = pos; // !!! this fixes test !!!
			file.Write(bytes, 0, bytes.Length);

			//Assert.AreEqual(pos + bytes.Length, file.Length); -- fails
		}

		using (var filestream = File.Open(filename, FileMode.Open, FileAccess.Read)) {
			var bb = new byte[bytes.Length];
			filestream.Position = pos;
			filestream.Read(bb, 0, bb.Length);
			Assert.AreEqual(bytes, bb);
		}
	}
}
