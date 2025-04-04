# Event streams

KurrentDB is purpose-built for event storage. Unlike traditional state-based databases, which retain only the most recent entity state, KurrentDB allows you to store each state alteration as an independent event.

These **events** are logically organized into **streams**, typically only one stream per entity.

## Metadata and reserved names

## Stream metadata

In KurrentDB, every stream and event is accompanied by metadata, distinguished by the **`$$`** prefix. For example, the stream metadata for a stream named **`foo`** is **`$$foo`**. You can modify specific metadata values and write your data to the metadata, which can be referenced in your code.

### Reserved names

KurrentDB uses a **`$`** prefix for all internal data, such as **`$maxCount`** in a stream's metadata. Do not use a **`$`** prefix for event names, metadata keys, or stream names, except as detailed below.

The supported internal settings are:

| Property name       | Description                                                                                                                                                                                                                                                                                                                                                            |
| :------------------ | :--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **`$maxAge`**       | Sets a sliding window based on dates. When data reaches a certain age, it automatically disappears from the stream and becomes eligible for scavenging. This value is set as an integer representing the number of seconds (must be >= 1).                                                                                                                             |
| **`$maxCount`**     | Sets a sliding window based on the number of items in the stream. When data reaches a certain length, it automatically disappears from the stream and becomes eligible for scavenging. This value is set as an integer representing the count of items (must be >= 1).                                                                                                 |
| **`$cacheControl`** | Controls the cache of the head of a stream. While most URIs in a stream are infinitely cacheable, the head, by default, will not cache. In some situations, it may be preferable to set a small amount of caching on the head to allow intermediaries to handle polls (e.g., 10 seconds). The argument is an integer representing the seconds to cache (must be >= 1). |

&nbsp;

::: tip
If you configure both **`$maxAge`** and **`$maxCount`**, events then become eligible for scavenging when either
condition is met. For example, if you set **`$maxAge`** to 10 seconds and **`$maxCount`** to 50,000, events
qualify for scavenging after either 10 seconds or the accumulation of 50,000 events. Affected items are removed once the scavenge process runs.
:::

**Security access control lists (ACLs)** are part of the **`$acl`** section in the stream metadata.

| Property name | Description                                     |
| :------------ | :---------------------------------------------- |
| **`$r`**      | Users with read permissions                     |
| **`$w`**      | Users with write permissions                    |
| **`$d`**      | Users with delete permissions                   |
| **`$mw`**     | Users with write permissions to stream metadata |
| **`$mr`**     | Users with read permissions to stream metadata  |

You can find more information on ACLs in the [access control lists documentation](../security/user-authorization.md#access-control-lists).

## Event metadata

All names starting with **`$`** are reserved for internal use. The currently supported reserved internal names include:

| Property name        | Description                                           |
| :------------------- | :---------------------------------------------------- |
| **`$correlationId`** | The application-level correlation ID for the message. |
| **`$causationId`**   | The application-level causation ID for the message.   |

Projections within KurrentDB adhere to both the **`correlationId`** and **`causationId`** patterns for any events they internally produce (e.g., through functions like `linkTo` and `emit`).

## Deleting streams and events

In KurrentDB, metadata can determine whether an event is deleted or not. You can use [stream metadata](#metadata-and-reserved-names) elements such as **`TruncateBefore`**, **`MaxAge`**, and **`MaxCount`** to
filter events marked as deleted. During a stream read, the index references the stream's metadata to determine if any events have been deleted.

::: note
In KurrentDB, you cannot selectively delete events from the middle of a stream. It only allows truncating the stream.
:::

When you delete a stream, KurrentDB offers two options: **soft delete** or **hard delete**.

**Soft delete** triggers scavenging, removing all events from the stream during the subsequent scavenging process. This allows for the reopening of the stream by appending new events.

It's worth noting that the **`$all`** stream circumvents index checking. Deleted events within this stream remain readable until a scavenging process removes them. To understand the prerequisites for successful event removal through scavenging, refer to the [scavenging guide](../operations/scavenge.md).

Even if a stream has been deleted, KurrentDB retains one event within the stream to indicate the stream's existence and provide information about the last event version. As a best practice, consider appending a specific event like **`StreamDeleted`** and then setting the **`MaxCount`** to 1 to delete the stream. Keep this in mind, especially when dealing with streams containing sensitive data that you want to erase thoroughly and without a trace.

### Soft delete and TruncateBefore

In event sourcing, **`TruncateBefore`** (or **`$tb`**) designates any event with an event number lower than its value as deleted. For instance, consider a stream with the following events:

```
0@test-stream
1@test-stream
2@test-stream
3@test-stream
```

Setting the **`TruncateBefore`** (or **`$tb`**) value to 3, results in reading only the last event:

```
3@test-stream
```

A **soft delete** leverages **`TruncateBefore`** (or **`$tb`**). When you delete a stream, **`TruncateBefore`**
(or **`$tb`**) is set to the [max long/Int64 value](https://docs.microsoft.com/en-us/dotnet/api/system.int64.maxvalue?view=net-5.0): 9223372036854775807.

If you attempt to read this soft-deleted stream the read returns a **`StreamNotFound`** or **`404`** result.

Appending to the stream later adds events starting from the last stream revision before deletion, incremented by one. Continuing with our previous example, a new event would be appended with event number 4, and only events from event number 4 onwards are visible when you read this stream.

### Max count and Max age

**Max count** (**`$maxCount`** and **`MaxCount`**) restricts the number of events you can read from a stream. Reading a stream with a max count of 5 allows you to retrieve only the last 5 events, regardless of the total events in the stream.

**Max age** (**`$maxAge`** and **`MaxAge`**) specifies the time an event can live in seconds. The age is
calculated at the time of the read. For example, if you read a stream with a **`MaxAge`** of 3 minutes, an event that has existed for 4 minutes won't be returned.

### Hard delete

A **hard delete** involves appending a **`tombstone`** event to the stream and permanently deleting it. This action prevents stream recreation or further appends. Tombstone events are appended with the event type **`$streamDeleted`**.
Any attempt to append or read a hard-deleted stream, the response is a **`StreamDeleted`** or **`410`** result.

While events in the deleted stream are subject to removal during a scavenge, the tombstone event remains.

::: note
Hard deletion of a stream is permanent; you cannot append or recreate it.
It is recommended to opt for soft deletion for streams unless you have a specific requirement for permanent removal.
:::

### Deleted events and projections

If you plan to use projections and delete streams, there are some considerations:

- In the context of **`$all`**, projections using **`fromAll`** read any deleted events that have not been scavenged. They also receive tombstone events from any hard deletions.

- System projections like [by category](projections/system.md#by-category) or [by event type](projections/system.md#by-event-type) produce new link events stored alongside
  the original event in the database. When original events are deleted, link events remain in the projected streams, but their links remain unresolved (with undefined values). You can ignore those events in the code logic.

## System events and streams

System streams and events begin with the `$` symbol. These streams and events are used for internal data, or for configuring specific parts of KurrentDB.

Metadata streams for system streams follow the same convention of prefixing the stream name with `$$`. Therefore metadata streams for system streams start with `$$$`.

By default, access to system streams is limited to members of the `$admins` group.

::: tip
Metadata streams (streams beginning with `$$` or `$$$`) are considered system streams.
:::

### **`$persistentSubscriptionConfig`**

**`$persistentSubscriptionConfig`** is a specialized paged stream that stores all configuration events for all
persistent subscriptions. It uses the **`$PersistentConfig`** system event type (previously **`PersistentConfig1`**), capturing configuration changes.
The event data includes:

| Property name   | Description                                   |
| :-------------- | :-------------------------------------------- |
| **`version`**   | Event data version                            |
| **`updated`**   | Date of the update                            |
| **`updatedBy`** | User responsible for the configuration update |
| **`maxCount`**  | The number of configuration events to retain  |
| **`entries`**   | Configuration items set by the event          |

### **`$all`**

**`$all`** is a dedicated paged stream containing all events. You can employ the same paged reading method described earlier to
read all events for a node by pointing the stream at _/streams/\$all_. Like any other stream, you can perform all operations, except posting to it.

### **`$settings`**

The **`$settings`** stream hosts a special ACL serving as the default ACL. It governs the default ACL for streams lacking ACLs and controls who can create streams within the system.

Learn more about the default ACL in the [access control lists](../security/user-authorization.md#default-acl) documentation.

### **`$stats`**

KurrentDB offers debugging and statistical information about a cluster within the **`$stats`** stream. Find out
more in [the stats guide](../diagnostics/README.md#statistics).

### **`$scavenges`**

**`$scavenges`** represents a specialized paged stream for all scavenge-related events. It uses the following system event
types:

- **`$scavengeIndexInitialized`**: An event that records the initialization of the scavenge index.
- **`$scavengeStarted`**: An event that records the beginning of a scavenge process. The event data contains:

  - **`scavengeId`**: Scavenge event ID
  - **`nodeEndpoint`**: Node address

- **`$scavengeCompleted`**: An event that records the completion of a scavenge process. The event data contains:
  - **`scavengeId`**: Scavenge event ID
  - **`nodeEndpoint`**: Node address
  - **`result`**: **`Success`**, **`Failed`**, **`Stopped`**
  - **`error`**: Error details
  - **`timeTaken`**: Duration of the scavenging process in milliseconds
  - **`spaceSaved`**: Space saved by scavenge process in bytes.

## Projections events and streams

These are the streams and event types that are specific to projections. Most projections streams are considered system streams as they start with a `$`.

User projections are able to interact with non-system streams depending on their code and configuration. You should avoid appending events directly to streams created by projections, as this may cause the projection to fault.

The system streams created by the projections subsystem are listed below.

### Streams created by system projections

The system projections create the following system streams:

- **`$streams`**: Output of the [`$streams` projection](./projections/system.md#streams-projection).
- **`$ce-`**: Output of the [`$by_category` projection](./projections/system.md#by-category).
- **`$et-`**: Output of the [`$by_event_type` projection](./projections/system.md#by-event-type).
- **`$et`**: Checkpoint stream for the [`$by_event_type` projection](./projections/system.md#by-event-type).
- **`$bc-`**: Output of the [`$by_correlation_id` projection](./projections/system.md#by-correlation-id).
- **`$category-`**: Output of the [`$stream_by_category` projection](./projections/system.md#stream-by-category).

### **`$projections-$all`**

This is the registry of all of the projections in KurrentDB. It contains a number of `$ProjectionCreated` and `$ProjectionDeleted` events, which form the list of available projections when replayed.

This stream contains the following event types:

- **`$ProjectionsInitialized`**: The first event written to the `$projections-$all` stream, which indicates that the projections system is ready to create new projections.
- **`$ProjectionCreated`**: A projection was created. The body contains the projection name, and the event number for this event becomes the projection's id.
- **`$ProjectionDeleted`**: A projection was deleted. The body contains the projection name.

### **`$projections-{projection_name}`**

The stream containing the definition for the `{projection_name}` projection. This stream contains the projection's configuration and code.

These streams contain the following event type:

- **`$ProjectionUpdated`**: The projection definition was updated.

### **`$projections-{projection_name}-checkpoint`** and **`$projections-{projection_name}-{partition}-checkpoint`**

The stream containing the checkpoints for the `{projection_name}` projection, specifically for the `{partition}` partition if it is included in the stream name.

The checkpoints differ depending on the selector used for the projection. For example, a projection using `fromAll` has a checkpoint containing an `$all` position, whereas a projection using `fromStream` has a checkpoint containing the stream position.

These streams contain the following event type:

- **`$ProjectionCheckpoint`**: The checkpoint for the projection. This includes the checkpoint position, the state at the time of the checkpoint, and the result at the time of the checkpoint.

### **`$projections-{projection_name}-result`** and **`$projections-{projection_name}-{partition}-result`**

The default stream containing the result of the `{projection_name}` projection, specifically for the `{partition}` partition if it is included in the stream name.

These streams are only created if the projection produces an [output state](./projections/custom.md#filters-and-transformations).

These streams contain the following event type:

- **`Result`**: The result of the projection.

### **`$projections-{projection_name}-emittedstreams`**

This stream typically contains the first event of each stream created by the `{projection_name}` projection. This stream is only created if [track emitted streams]() is enabled.

These streams contain the following event type:

- **`$StreamTracked`**: The name of the stream created by the projection.

### **`$projections-{projection_name}-emittedstreams-checkpoint`**

If tracked emitted streams for the `{projection_name}` projection have been deleted, this stream will contain a checkpoint for where the emitted streams were deleted up to.

These streams contain the following event type:

- **`$ProjectionCheckpoint`**: The checkpoint for where the emitted streams have been deleted up to.

### **`$projections-{projection_name}-order`**

This stream contains the ordering for the `{projection_name}` projection. Some projections, such as ones using `fromStreams` or `fromCategories`, need to be ordered before they can be processed so that the projection can guarantee that it will process the events in the same order after restarts.

This stream contains LinkTo events, pointing to the original events.

### **`$projections-{projection_name}-partitions`**

This stream contains a list of the partitions for the `{projection_name}` projection. This stream will only exist if the projection has [multiple partitions](./projections/custom.md#filters-and-transformations).

These streams contain the following event type:

- **`$partition`**: The name of the partition.
