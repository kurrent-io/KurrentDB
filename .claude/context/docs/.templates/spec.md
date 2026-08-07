---
title: <Feature name> — Tech Spec
status: draft            # draft | review | accepted | superseded
authors: [username]
date: <YYYY-MM-DD>
tags: []
---

# Tech Spec — <Feature name>

> The HOW — distilled from the design space, **implementation-grade**. A qualified reader should be able
> to build this cold. Current-state document: keep it in sync with the code as it lands. Diagrams and
> supporting material go in `spec/refs/`.

## Overview

The approach in a few sentences.

## Design

Architecture, data model, public API surface, control flow. Reference diagrams in `refs/`.

## Alternatives Considered

What was evaluated and why it lost. The design space holds the full discussion; capture the verdict here.

## Edge Cases & Failure Modes

Boundaries, empty/huge inputs, error handling, concurrency windows, compatibility constraints.

## Testing

What proves it correct — the test surface and the key cases that must pass.

## Rollout

Migration, backwards-compatibility, feature flags, sequencing, and how to roll back.
