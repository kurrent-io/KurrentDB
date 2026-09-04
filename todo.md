# TODO: Kontrol Plane bring-up

Open problems found while getting a cluster running. Not future work - these block or mislead now.
Rough priority order.

## node3's Kontroller never catches up with the Raft log - ROOT CAUSE FOUND, FIXED

Snapshot install failed on the receiving node every single time, so the follower could never advance
past the leader's compaction point and the cluster livelocked. The 7-hour overnight run shows the
cycle exactly, seventeen times, perfectly regular:

    00:00:00  retry 2736 -> 00:28:37 retry 2049   (688 retries, one entry per ~2.5s)
    00:28:39  Installing snapshot at index 2049 for member ...:3113
    00:28:48  Transition to Candidate, term 112      <- leader steps down 9s later
    00:28:50  Transition to Leader, term 113         <- nextIndex resets to the top
    00:28:50  retry 2886 -> 01:03:42 retry 2049      <- and round again

and on node3 at 00:28:39, at the instant of every one of those installs:

    IO Error: Cannot open file "D:\temp\timot\o3vtrd0x.aeg": The process cannot access the file
    because it is being used by another process. File is already open in KurrentDB.exe (PID 65120)
      at ClusterState.LoadFromFile -> ClusterStateMachine.InstallSnapshot -> InstallSnapshotAsync

`InstallSnapshotAsync` wrote the incoming snapshot to a temp file opened `FileShare.None` and called
`InstallSnapshot(fileName)` while that `FileStream` was still open, so DuckDB's `ATTACH` hit a sharing
violation from our own process. Fixed by closing the stream before the attach. Note it is the *same*
process holding the handle, which is why the error text is confusing.

- [x] Close the file before `ATTACH` (`ClusterStateMachine.Snapshot.cs`).
- [ ] Re-run nodes 1 and 3 and confirm node3 converges after one snapshot install.
- [x] Persist the received snapshot and publish it as `_persistentSnapshot` (second bug, found once the
      first was fixed: node3 then died with `MissingPageException: WAL page 20 doesn't exist on the
      disk` from `PreVoteAsync -> IsUpToDateAsync`). `InstallSnapshotAsync` applied the leader's
      snapshot to `_state` and deleted the file, never setting `_persistentSnapshot` - unlike
      `Recover` and `SaveSnapshot`, which both set it. So the state machine advanced to 2049 while
      `ISnapshotManager.Snapshot` still reported the older snapshot node3 recovered with, and the WAL
      pages in between were gone. It now writes into `_location/<index>` and publishes it.
- [x] Confirmed: node3 joins with both fixes and *without* wiping `db/kontroller`. Nothing on disk was
      ever corrupt - the old snapshot file and the WAL were both valid, and the whole fault was the
      in-memory `_persistentSnapshot` field not moving when a snapshot was installed.
- [ ] `InstallSnapshotAsync` swallowed nothing - the exception surfaced only as TcpServer's
      "Failed to process request", at `Error` but with no mention of snapshots. A failed snapshot
      install should say so explicitly; it is not recoverable by retrying the same way.
- [ ] Still raise with Roman: one entry per ~2.5s of backtracking is not a viable catch-up rate even
      when the snapshot path works, and the leader stepping down ~9s into an install means a slow
      install can never complete inside one term. Both need bounding.
- [ ] Earlier `ArgumentOutOfRangeException` in `WriteAheadLog.AppendAndCommitSlowAsync` is unexplained
      and may be independent. ("Follower loop stopped with error" is a red herring - every occurrence
      in every log is just `OperationCanceledException` at cancellation.)

## Snapshot restore can violate a foreign key and kill the host

Seen once at startup, gone on a retry:

    Constraint Error: Violates foreign key constraint because key "id: main" does not exist in the
    referenced table
      at ClusterState.LoadFromFile -> ClusterStateMachine.InstallSnapshot -> Recover
      -> RaftKontroller..ctor -> ClusterVNode..ctor -> Host terminated unexpectedly

`InstallSnapshot` builds an empty `ClusterState` - the constructor opens an in-memory DuckDB and
applies no schema - and `LoadFromFile` then runs `COPY FROM DATABASE snapshot TO memory`, which
creates the tables with their constraints and copies the rows in one statement. `node` has
`FOREIGN KEY (database_id) REFERENCES database (id)`, so if the rows of `node` are copied before
those of `database` the constraint fails. Intermittent because it depends on the order DuckDB
happens to visit the tables in, not on the contents of the snapshot.

- [ ] Raise with Roman. Dropping the foreign key is the smallest fix and it guards a single row;
      copying the tables explicitly in dependency order is the safer one.
- [ ] Note the blast radius: this throws from `RaftKontroller`'s constructor, so it does not degrade
      the Kontrol Plane, it terminates the host - and nothing retries, so a snapshot that reliably
      copies in the wrong order would leave a node unstartable.

## Sandbox clock skew silently skipped builds

The agent sandbox clock runs ~9 hours behind the host, so files written by Claude are stamped in the
past. MSBuild compares source against output timestamps, so an edit could be newer in content but
older by mtime than the last build and be skipped without any error. This is why the
`BumpEpochAsync` timeout did not appear in the 16:51 run despite being in the source.

- [ ] Build once with `--no-incremental`. Everything edited today is suspect, including the announce
      upsert in `GrpcKontrollerServer`, `RaftKontroller.Impl`, `DatabaseStateHandler`, `ClusterVNode`,
      `ClusterVNodeStartup` and the TLS files.
- [ ] Decide whether to fix the sandbox clock or keep stamping files explicitly.

## Stale instance ids are persisted in the Kontrol Plane

`AnnounceDatabaseNode` used `TryAddDatabaseNodeAsync` (`INSERT OR IGNORE`), so a node that restarted
kept its dead process's `InstanceId` in the `node` table forever. That id then reached followers in
`LeaderAppointed`, the real leader rejected their subscription as addressed to someone else, and the
follower took `Application.Exit` on the "leader is either null or wrong" assertion.

Fixed by switching announce to `AddOrUpdateDatabaseNodeAsync`, and the assertion no longer exits for
`ReplicaSubscriptionRetry`. But:

- [ ] Wipe `db/kontroller` on every node before the next clean run - the bad `instance_id` rows are
      already persisted and the fix only affects the next announce.

## Appointment state does not survive a Kontrol Plane failover

`_appointmentState` is in-memory on the Kontrol Plane leader and cleared when leadership moves, so a
new leader rejected the incumbent's renewals and ended its leadership for no reason.

- [ ] Not fixed. `IsAppointmentRequired` still returns `true` whenever `_appointmentState` has no
      entry for the database, so a fresh leader re-appoints regardless of what the replicated
      `node.is_leader` / `instance_id` / `database.epoch` rows already say. Recovery has to be read
      back from those rows.

## Data plane leaders can be appointed but never told

If the announce stream lags behind the epoch clock, every snapshot arrives stale, `ChangeStateAsync`
discards it, the node never leads, never renews, and the appointment expires - repeatedly, once per
second. Caused here by `Nodes` doing a metadata round trip per member per message, since an
unreachable member had nothing cached to make it cheap.

- [ ] Confirm the connect timeout and the `Status is not Available` skip have actually removed the
      per-message stall, once building properly again.
- [ ] The staleness check itself is correct and should stay; it is the stream lag that was wrong.

## Diagnosis is hampered by the log configuration

`"DotNext.Net.Cluster": "Error"` in `logconfig.json` hid every transition, `UnresponsiveMemberDetected`
and the replication failures above, which cost a whole debugging session.

- [ ] Decide a level that keeps the useful events without the per-timeout noise. `Information` gave
      exactly what was needed.

## `AnnounceNodeAsync` spins with no backoff

node3 logged 34,989 iterations of its retry loop over 130 seconds, peaking at 854/s, while the
Kontrol Plane was unreachable. The `Error`-level lines are temporary debug logging, but the spin is
real.

- [ ] Add a backoff (or await the connection) so a node that cannot reach the Kontrol Plane does not
      burn a core retrying.
