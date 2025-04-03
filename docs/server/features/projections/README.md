---
dir:
  text: Projections
  order: 5
title: Introduction to projections
---

Projections is a KurrentDB subsystem that lets you append new events or link existing events to streams in
a reactive manner.

Projections are good at solving one specific query type, a category known as 'temporal correlation queries'.
This query type is common in business systems and few can execute these queries well.

::: tip 
Projections require the event body to be in JSON.
:::

## Business case examples

For example. You are looking for how many Twitter users said "happy" within 5 minutes of the word "foo coffee
shop" and within 2 minutes of saying "london".

This is the type of query that projections can solve. Let's try a more complex business problem.

As a medical research doctor you want to find people diagnosed with pancreatic cancer within the last year.
During their treatment a patient should not have had any proxies for a heart condition such as taking aspirin
every morning. Within three weeks of their diagnosis they should have been put on treatment X. Within one
month after starting the treatment they should have failed with a lab result that looks like L1. Within
another six weeks they should have been put on treatment Y, and within four weeks failed that treatment with a
lab result that looks like L2.

You can use projections in nearly all examples of near real-time complex event processing. There are a large
number of problems that fit into this category from monitoring of temperature sensors, to reacting to changes
in the stock market.

It's important to remember the types of problems that projections help to solve. Many problems are not a good
fit for projections and are better served by hosting another read model populated by a catchup subscription.

## Continuous querying

Projections support the concept of continuous queries. When running a projection you can choose whether the
query should run and give you all results present, or whether the query should continue running into the
future finding new results as they happen and updating its result set.

In the medical example above the doctor could leave the query running to be notified of any new patients that
meet the criteria. The output of all queries is a stream, you can listen to this stream like any other stream.

## Types of projections

There are two types of projections in KurrentDB:

- [Built in (system) projections](system.md)
- [User-defined JavaScript projections](custom.md) which you create via the API or the admin
  UI

## Performance impact

Keep in mind that all projections emit events as a reaction to events that they process. We call this effect _write amplification_
because emitting new events or link events creates additional load on the server IO.

Some system projections emit link events to their streams for each event appended to the database. These
projections are By Category, By Event Type and By Correlation Id. If all those three projections are enabled
and started, adding one event to the database will, in fact, produce three additional events and, therefore,
quadruples the number of write operations.

System projections `$streams` and `$stream-by-category` produce new events too, either per each new stream or
per new stream category. If your system has a lot of small streams, the `$streams` system projection would
also amplify writes significantly.

Custom projections create the most significant write amplification since they produce new events or link
events, which in turn get processed by system projections.

Projections only run on a leader node of the cluster due to consistency concerns. It creates more CPU and IO
load on the leader node compared to follower nodes.

## Limitations

Streams where projections emit events cannot be used to append events from applications. When this happens,
the projection will detect events not produced by the projection itself and it will break.

The reason projections exclusively own their streams is that otherwise they would lose all predictability. The
projection would no longer have any idea what should be in that stream. For example, when a projection starts
up from a checkpoint, it first goes through all the events after that checkpoint and checks them against the
emitted stream. By doing this, the projection can understand if it up to the last event and can continue from
where it left off. On top of that, the projection can verify that everything is in order, no events missing,
etc. If anyone can append to the emitted streams, then the projection would have no idea where it got to last
in terms of processing. Therefore, it can no longer trust that the projection itself emitted that event or if
something else did.

### Impact of resetting projections
Resetting a projection in KurrentDB will soft-delete the output streams associated with the projection. If 'TrackEmittedStreams' is enabled when the projection is created, it will also allow the projection subsystem to truncate all streams created by the projection.

The checkpoint will also be reset. This means that the projection will start processing events from the beginning of the event stream and not from the latest checkpoint.