---
order: 3
---

# Backup and restore

Backing up a KurrentDB database is straightforward but relies on carrying out the steps below in the
correct order.

## Types of backups

There are two main ways to perform backups:

- **Disk snapshots**: If your infrastructure is virtualized, [disk snapshots](#disks-snapshot) are an option and the easiest way to perform backup and
  restore operations.

- **Regular file copy**:

    - [Simple full backup](#simple-full-backup--restore): when the DB size is small and the frequency of appends is low.
    - [Differential backup](#differential-backup--restore): when the DB size is large or the system has a high append frequency.

## Backup and restore best practices

- Backing up one node is recommended. However, ensure that the node you choose to back up is **up to date** and **connected to the cluster**.

- For additional safety, you can also back up at least a quorum of nodes.

- Do not attempt to back up a node using file copy at the same time a scavenge operation is running. Your backup script should [stop any ongoing scavenge](#stopping-an-ongoing-scavenge-before-taking-a-backup) on a node before taking a backup.

- [Read-only replica](../configuration/cluster.md#read-only-replica) nodes may be used as a backup source.

- When either running a backup or restoring, do not mix backup files of different nodes.

- A restore must happen on a _stopped_ node.

- The restore process can happen on any node of a cluster.

- You can restore any number of nodes in a cluster from the same backup source. It means, for example, in the
  event of a non-recoverable three node cluster, that the same backup source can be used to restore a completely new
  three node cluster.

- When you restore a node that was the backup source, perform a full backup after recovery.

## Database files information

By default, there are two directories containing data that needs to be included in the backup:

- `db/ ` where the data is located
- `index/ ` where the indexes are kept

The exact name and location are dependent on your configuration.

- `db/ ` contains:
    - the chunks files named `chk-X.Y` where `X` is the chunk number and `Y` the version.
    - the checkpoints files `*.chk` (`chaser.chk`, `epoch.chk`, `proposal.chk`, `truncate.chk`, `writer.chk`)
- `index/ ` contains:
    - the index map: `indexmap`
    - the indexes: UUID named files , e.g `5a1a8395-94ee-40c1-bf93-aa757b5887f8`

## Disks snapshot

If the `db/ ` and `index/ ` directories are on the same volume, a snapshot of that volume is enough.

However, if they are on different volumes, take first a snapshot of the volume containing the `index/ `
directory and then a snapshot of the volume containing the `db/ ` directory.

## Simple full backup & restore

### Backup

1. Copy any index checkpoint files (`<index directory>/**/*.chk`) to your backup location.
2. Copy the other index files to your backup location (the rest of `<index directory>`, excluding the checkpoints).
3. Copy the database checkpoint files (`*.chk`) to your backup location.
4. Copy the chunk files (`chunk-X.Y`) to your backup location.

For example, with a database in `data` and index in `data/index`:

```bash:no-line-numbers
rsync -aIR data/./index/**/*.chk backup
rsync -aI --exclude '*.chk' data/index backup
rsync -aI data/*.chk backup
rsync -a data/*.0* backup
```

### Restore

1. Ensure the KurrentDB process is stopped. Restoring a database on running instance is not possible and,
   in most cases, will lead to data corruption.
2. Copy all files to the desired location.
3. Create a copy of `chaser.chk` and call it `truncate.chk`. This effectively overwrites the
   restored `truncate.chk`.

## Differential backup & restore

The following procedure is designed to minimize the backup storage space, and can be used to do a full and
differential backup.

### Backup

First backup the index:

1. If there are no files in the index directory (apart from directories), go to step 7.
2. Copy the `index/indexmap` file to the backup. If the source file does not exist, repeat until it does.
3. Make a list `indexFiles` of all the `index/<GUID>` and `index/<GUID>.bloomfilter` files in the source.
4. Copy the files listed in `indexFiles` to the backup, skipping file names already in the backup.
5. Compare the contents of the `indexmap` file in the source and the backup. If they are different (i.e. the
   `indexmap` file has changed since step 2 or no longer exists), go back to step 2.
6. Remove `index/<GUID>` and `index/<GUID>.bloomfilter` files from the backup that are not listed
   in `indexFiles`.
7. Copy the `index/stream-existence/streamExistenceFilter.chk` file (if present) to the backup.
8. Copy the `index/stream-existence/streamExistenceFilter.dat` file (if present) to the backup.
9. Copy the `index/scavenging/scavenging.db` file (if present) to the backup. It should be the only file in the `scavenging` directory.

Then backup the log:

1. Rename the last chunk in the backup to have a `.old` suffix. e.g. rename `chunk-000123.000000`
   to `chunk-000123.000000.old`.
2. Copy `chaser.chk` to the backup.
3. Copy `epoch.chk` to the backup.
4. Copy `writer.chk` to the backup.
5. Copy `proposal.chk` to the backup.
6. Make a list `chunkFiles` of all chunk files (`chunk-X.Y`) in the source.
7. Copy the files listed in `chunkFiles` to the backup, skipping file names already in the backup. All files
   should copy successfully. None should have been deleted since scavenge is not running.
8. Remove any chunks from the backup that are not in the `chunksFiles` list. This will include the `.old`
   file from step 1.

### Restore

1. Ensure the KurrentDB process is stopped. Restoring a database on running instance is not possible and,
   in most cases, will lead to data corruption.
2. Copy all files to the desired location.
3. Create a copy of `chaser.chk` and call it `truncate.chk`.

## Stopping an ongoing scavenge before taking a backup
It is extremely important to stop any ongoing scavenge before taking a backup. If this step is not followed, the backed up data may have missing or corrupted files. Your backup script can include the following steps to ensure that any ongoing scavenge is stopped before a node is backed up:

1. Do an HTTP `GET` request to `/admin/scavenge/current` or `/admin/scavenge/last` to determine if there is an ongoing scavenge on the node.
2. If there is one, do an HTTP `DELETE` request to `/admin/scavenge/{scavengeId}` to abort the scavenge. `{scavengeId}` can be obtained from the HTTP response received in the first step.
3. If you are using the [auto-scavenge](./auto-scavenge.md) feature, it is recommended to pause auto-scavenge by doing a `POST` request to `/auto-scavenge/pause`. This will ensure that no new scavenges are launched on the node while it is being backed up.
4. You can now back up the node's data
5. If you are using the auto-scavenge feature, you can resume auto-scavenge by doing a `POST` request to `/auto-scavenge/resume`.
6. Finally, you can optionally resume the scavenge that was aborted by starting a new one. This can be done with a `POST` request to `/admin/scavenge`. If you are using the auto-scavenge feature, it will resume the scavenge automatically.

## Other options for data recovery

### Additional node (aka Hot Backup)

Increase the cluster size from 3 to 5 to keep further copies of data. This increase in the cluster size will
slow the cluster's writing performance as two follower nodes will need to confirm each write.

Alternatively, you can use a [read-only replica](../configuration/cluster.md#read-only-replica) node, which is not a part of
the cluster. In this case, the write performance will be minimally impacted.

### Alternative storage

Set up a durable subscription that writes all events to another storage mechanism such as a key/value or
column store. These methods would require a manual set up for restoring a cluster node or group.

### Backup cluster

Use a second KurrentDB cluster as a backup. Such a strategy is known as a primary/secondary back up scheme.

The primary cluster asynchronously pushes data to the second cluster using a durable subscription. The second
cluster is available in case of a disaster on the primary cluster.

If you are using this strategy, we recommend you only support manual fail over from primary to secondary as
automated strategies risk causing a [split brain](http://en.wikipedia.org/wiki/Split-brain_%28computing%29)
problem.
