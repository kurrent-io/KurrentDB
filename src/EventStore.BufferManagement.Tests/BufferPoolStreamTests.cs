// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using NUnit.Framework;

namespace EventStore.BufferManagement.Tests;

public abstract class has_buffer_pool_fixture : has_buffer_manager_fixture {
	protected BufferPool BufferPool;

	[SetUp]
	public override void Setup() {
		base.Setup();
		BufferPool = new BufferPool(10, BufferManager);
	}
}

[TestFixture]
public class when_insantiating_a_buffer_pool_stream : has_buffer_pool_fixture {
	[Test]
	public void a_null_buffer_pool_throws_an_argumentnullexception() {
		Assert.Throws<ArgumentNullException>(() => new BufferPoolStream(null));
	}

	[Test]
	public void the_internal_buffer_pool_is_set() {
		using BufferPoolStream stream = new BufferPoolStream(BufferPool);
		Assert.AreEqual(BufferPool, stream.BufferPool);
	}
}

[TestFixture]
public class when_reading_from_the_stream : has_buffer_pool_fixture {
	[Test]
	public void position_is_incremented() {
		using BufferPoolStream stream = new BufferPoolStream(BufferPool);
		stream.Write(new byte[500], 0, 500);
		stream.Seek(0, SeekOrigin.Begin);
		Assert.AreEqual(0, stream.Position);
		stream.Read(new byte[50], 0, 50);
		Assert.AreEqual(50, stream.Position);
	}

	[Test]
	public void a_read_past_the_end_of_the_stream_returns_zero() {
		using BufferPoolStream stream = new BufferPoolStream(BufferPool);
		stream.Write(new byte[500], 0, 500);
		stream.Position = 0;
		int read = stream.Read(new byte[500], 0, 500);
		Assert.AreEqual(500, read);
		read = stream.Read(new byte[500], 0, 500);
		Assert.AreEqual(0, read);
	}

	[Test]
	public void reading_from_the_stream_with_StreamCopyTo_returns_all_data() {
		using BufferPoolStream stream = new BufferPoolStream(BufferPool);
		int size = 20123;
		stream.Write(new byte[size], 0, size);
		stream.Position = 0;

		using var destination = new MemoryStream();
		stream.CopyTo(destination);
		Assert.AreEqual(destination.Length, size);
	}
}

[TestFixture]
public class when_writing_to_the_stream : has_buffer_pool_fixture {
	[Test]
	public void position_is_incremented() {
		using BufferPoolStream stream = new BufferPoolStream(BufferPool);
		stream.Write(new byte[500], 0, 500);
		Assert.AreEqual(500, stream.Position);
	}
}

[TestFixture]
public class when_seeking_in_the_stream : has_buffer_pool_fixture {
	[Test]
	public void from_begin_sets_relative_to_beginning() {
		using BufferPoolStream stream = new BufferPoolStream(BufferPool);
		stream.Write(new byte[500], 0, 500);
		stream.Seek(22, SeekOrigin.Begin);
		Assert.AreEqual(22, stream.Position);
	}

	[Test]
	public void from_end_sets_relative_to_end() {
		using BufferPoolStream stream = new BufferPoolStream(BufferPool);
		stream.Write(new byte[500], 0, 500);
		stream.Seek(-100, SeekOrigin.End);
		Assert.AreEqual(400, stream.Position);
	}

	[Test]
	public void from_current_sets_relative_to_current() {
		using BufferPoolStream stream = new BufferPoolStream(BufferPool);
		stream.Write(new byte[500], 0, 500);
		stream.Seek(-2, SeekOrigin.Current);
		stream.Seek(1, SeekOrigin.Current);
		Assert.AreEqual(499, stream.Position);
	}

	[Test]
	public void a_negative_position_throws_an_argumentexception() {
		using BufferPoolStream stream = new BufferPoolStream(BufferPool);
		Assert.Throws<ArgumentOutOfRangeException>(() => { stream.Seek(-1, SeekOrigin.Begin); });
	}

	[Test]
	public void seeking_past_end_of_stream_throws_an_argumentexception() {
		using BufferPoolStream stream = new BufferPoolStream(BufferPool);
		stream.Write(new byte[500], 0, 500);
		Assert.Throws<ArgumentOutOfRangeException>(() => { stream.Seek(501, SeekOrigin.Begin); });
	}
}
