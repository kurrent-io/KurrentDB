---
name: kontext-validation-decorator-defect
description: Open defect + pending decision — KontextMemoryValidationDecorator is registered circularly; Sérgio is deciding whether to fix the registration or drop decorators and inline validation
metadata: 
  node_type: memory
  type: project
  originSessionId: 61716747-5d17-4628-990c-5011709058cd
  modified: 2026-08-21T18:30:47.745Z
---

`KontextMemoryWireUp.cs` registers `TryAddSingleton<IKontextMemory, KontextMemoryValidationDecorator>()`
while the decorator's constructor takes `IKontextMemory` — the container resolves the decorator as its
own inner service. Nothing in the suite resolves `IKontextMemory`, so it has never thrown. Found
2026-08-21.

RULED 2026-08-21: inline `validation.Validate(request)` into the service, no decorators. Done for
records — `KontextRecordsValidationDecorator` deleted, `KontextRecords` takes `RequestValidationService`
and validates as its first line. MEMORY STILL PENDING and still carries the circular registration.

**Why:** the decorator's separation was already breached (`KontextMemory.EnsureReferencedMemoriesResolveAsync`
throws `RequestValidationException` from inside the service), and MS DI has no decoration support, so
the pattern cost a hand-written factory plus this footgun to buy swappability nobody used.

**How to apply:** do the memory side the same way — inject `RequestValidationService` into
`KontextMemory`, validate first in each method, delete `KontextMemoryValidationDecorator`, and register
`TryAddSingleton<IKontextMemory, KontextMemory>()`. See [[kontext-reloaded-canonical-model]].
