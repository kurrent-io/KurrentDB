// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Helpers;

[TestFixture]
public class length_prefix_suffix_framer_should {
	[Test]
	public void correctly_frame_byte_array() {
		var framer = new LengthPrefixSuffixFramer();
		var data = new byte[] {0x7, 0x17, 0x27};
		var framedData = MergeBytes(framer.FrameData(new ArraySegment<byte>(data)));

		Assert.AreEqual(11, framedData.Length);

		Assert.AreEqual(0x03, framedData[0]);
		Assert.AreEqual(0x00, framedData[1]);
		Assert.AreEqual(0x00, framedData[2]);
		Assert.AreEqual(0x00, framedData[3]);

		Assert.AreEqual(0x07, framedData[4]);
		Assert.AreEqual(0x17, framedData[5]);
		Assert.AreEqual(0x27, framedData[6]);

		Assert.AreEqual(0x03, framedData[7]);
		Assert.AreEqual(0x00, framedData[8]);
		Assert.AreEqual(0x00, framedData[9]);
		Assert.AreEqual(0x00, framedData[10]);
	}

	private byte[] MergeBytes(IEnumerable<ArraySegment<byte>> frameData) {
		var bytes = new List<byte>();
		foreach (var segm in frameData) {
			for (int i = segm.Offset; i < segm.Offset + segm.Count; ++i) {
				bytes.Add(segm.Array[i]);
			}
		}

		return bytes.ToArray();
	}

	[Test]
	public async Task unframe_record_when_provided_exactly_enough_data_in_one_call() {
		int unframedCnt = 0;
		var framer = new LengthPrefixSuffixFramer();
		framer.RegisterMessageArrivedCallback(async (r, token) => {
			unframedCnt += 1;
			Assert.AreEqual(new byte[] {0x07, 0x17, 0x27}, await ReadAll(r, token));
		});

		await framer.UnFrameData(new ArraySegment<byte>([
			0x03, 0x00, 0x00, 0x00,
			0x07, 0x17, 0x27,
			0x03, 0x00, 0x00, 0x00
		]), CancellationToken.None);

		Assert.AreEqual(1, unframedCnt);
	}

	[Test]
	public async Task unframe_record_when_provided_with_small_chunks_of_data_at_a_time() {
		int unframedCnt = 0;
		var framer = new LengthPrefixSuffixFramer();
		framer.RegisterMessageArrivedCallback(async (r, token) => {
			unframedCnt += 1;
			Assert.AreEqual(new byte[] { 0x07, 0x17, 0x27 }, await ReadAll(r, token));
		});

		await framer.UnFrameData(new ArraySegment<byte>(new byte[] {0x03, 0x00}), CancellationToken.None);
		await framer.UnFrameData(new ArraySegment<byte>(new byte[] {0x00, 0x00}), CancellationToken.None);
		await framer.UnFrameData(new ArraySegment<byte>(new byte[] {0x07, 0x17, 0x27}), CancellationToken.None);
		await framer.UnFrameData(new ArraySegment<byte>(new byte[] {0x03, 0x00}), CancellationToken.None);

		Assert.AreEqual(0, unframedCnt);
		await framer.UnFrameData(new ArraySegment<byte>(new byte[] {0x00, 0x00}), CancellationToken.None);
		Assert.AreEqual(1, unframedCnt);
	}

	[Test]
	public async Task unframe_two_consecutive_records() {
		int unframedCnt = 0;
		var framer = new LengthPrefixSuffixFramer();
		framer.RegisterMessageArrivedCallback(async (r, token) => {
			if (unframedCnt == 0)
				Assert.AreEqual(new byte[] {0x07, 0x17, 0x27}, await ReadAll(r, token));
			else if (unframedCnt == 1)
				Assert.AreEqual(new byte[] {0x05, 0x15}, await ReadAll(r, token));
			else
				Assert.Fail();

			unframedCnt += 1;
		});

		await framer.UnFrameData(new ArraySegment<byte>([
			0x03, 0x00, 0x00, 0x00,
			0x07, 0x17, 0x27
		]), CancellationToken.None);

		Assert.AreEqual(0, unframedCnt);

		await framer.UnFrameData(new ArraySegment<byte>([
			0x03, 0x00, 0x00, 0x00,
			0x02, 0x00, 0x00, 0x00,
			0x05, 0x15
		]), CancellationToken.None);

		Assert.AreEqual(1, unframedCnt);

		await framer.UnFrameData(new ArraySegment<byte>([0x02, 0x00, 0x00, 0x00]), CancellationToken.None);

		Assert.AreEqual(2, unframedCnt);
	}

	[Test]
	public async Task discard_data_when_reset_and_continue_unframing_from_blank_slate() {
		int unframedCnt = 0;
		var framer = new LengthPrefixSuffixFramer();
		framer.RegisterMessageArrivedCallback(async (r, token) => {
			if (unframedCnt == 0)
				Assert.AreEqual(new byte[] {0x07, 0x17, 0x27}, await ReadAll(r, token));
			else if (unframedCnt == 1)
				Assert.AreEqual(new byte[] {0x05, 0x15}, await ReadAll(r, token));
			else
				Assert.Fail();

			unframedCnt += 1;
		});

		await framer.UnFrameData(new ArraySegment<byte>([
			0x03, 0x00, 0x00, 0x00,
			0x07, 0x17, 0x27,
			0x03, 0x00, 0x00, 0x00,
			0xAA, 0xBB, 0xCC, 0x00,
			0x01, 0x02, 0x03, 0x04,
			0x05, 0x06, 0x07, 0x08
		]), CancellationToken.None);

		Assert.AreEqual(1, unframedCnt);

		framer.Reset();

		await framer.UnFrameData(new ArraySegment<byte>([
			0x02, 0x00, 0x00, 0x00,
			0x05, 0x15,
			0x02, 0x00, 0x00, 0x00
		]), CancellationToken.None);

		Assert.AreEqual(2, unframedCnt);
	}

	private static ValueTask<byte[]> ReadAll(ReadOnlySequence<byte> br, CancellationToken token)
		=> token.IsCancellationRequested
			? ValueTask.FromCanceled<byte[]>(token)
			: ValueTask.FromResult(br.ToArray());
}
