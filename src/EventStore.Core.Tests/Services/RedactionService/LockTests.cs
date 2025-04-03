// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Data.Redaction;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RedactionService;


[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class LockTests<TLogFormat, TStreamId> : RedactionServiceTestFixture<TLogFormat,TStreamId> {
	private async Task<RedactionMessage.AcquireChunksLockCompleted> TryLock() {
		var e = new TcsEnvelope<RedactionMessage.AcquireChunksLockCompleted>();
		RedactionService.Handle(new RedactionMessage.AcquireChunksLock(e));
		return await e.Task;
	}

	private async Task<RedactionMessage.ReleaseChunksLockCompleted> TryUnlock(Guid lockId) {
		var e = new TcsEnvelope<RedactionMessage.ReleaseChunksLockCompleted>();
		RedactionService.Handle(new RedactionMessage.ReleaseChunksLock(e, lockId));
		return await e.Task;
	}

	[Test]
	public async Task can_lock() {
		var msg = await TryLock();
		Assert.AreEqual(AcquireChunksLockResult.Success, msg.Result);
	}

	[Test]
	public async Task can_unlock() {
		var lockMsg = await TryLock();
		Assert.AreEqual(AcquireChunksLockResult.Success, lockMsg.Result);

		var unlockMsg = await TryUnlock(lockMsg.AcquisitionId);
		Assert.AreEqual(ReleaseChunksLockResult.Success, unlockMsg.Result);
	}

	[Test]
	public async Task cannot_unlock_with_wrong_id() {
		var lockMsg = await TryLock();
		Assert.AreEqual(AcquireChunksLockResult.Success, lockMsg.Result);

		var unlockMsg = await TryUnlock(Guid.NewGuid());
		Assert.AreEqual(ReleaseChunksLockResult.Failed, unlockMsg.Result);
	}

	[Test]
	public async Task cannot_unlock_twice_with_same_id() {
		var lockMsg = await TryLock();
		Assert.AreEqual(AcquireChunksLockResult.Success, lockMsg.Result);

		var unlockMsg1 = await TryUnlock(lockMsg.AcquisitionId);
		Assert.AreEqual(ReleaseChunksLockResult.Success, unlockMsg1.Result);

		var unlockMsg2 = await TryUnlock(lockMsg.AcquisitionId);
		Assert.AreEqual(ReleaseChunksLockResult.Failed, unlockMsg2.Result);
	}

	[Test]
	public async Task cannot_unlock_when_unlocked() {
		var msg = await TryUnlock(Guid.NewGuid());
		Assert.AreEqual(ReleaseChunksLockResult.Failed, msg.Result);
	}

	[Test]
	public async Task cannot_lock_when_locked() {
		var msg = await TryLock();
		Assert.AreEqual(AcquireChunksLockResult.Success, msg.Result);

		msg = await TryLock();
		Assert.AreEqual(AcquireChunksLockResult.Failed, msg.Result);
	}
}
