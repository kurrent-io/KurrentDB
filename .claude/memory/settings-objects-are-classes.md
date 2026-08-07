---
name: settings-objects-are-classes
description: "Config/settings objects (anything bound from IConfiguration) should be mutable classes, not records"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 5e6f16df-d272-4599-b8d8-7d3f46821fc4
---

Objects used as settings — especially anything bound from `IConfiguration` — should be **plain mutable classes** with `{ get; set; }` properties and a parameterless constructor, not `record`s.

**Why:** the config-binding source generator constructs `new T()` then assigns each property, so `required` / init-only members fail with **CS9035** ("required member must be set in the object initializer"). A record with `required`/`init` forces awkward workarounds (positional records, etc.); a plain class with settable props + defaults binds first try, matches the .NET options/settings convention, and reflects the mutable semantics settings actually have. (Sérgio: "you wouldn't need to change anything if it was a class.")

**How to apply:** for any config-bound / settings type, use `public sealed class` with `public T Prop { get; set; }` and non-nullable defaults (`= ""`, `= []`). Reserve `record` for immutable data that isn't config-bound. The `Action<TOptions>` configure objects (e.g. `SentencePieceOnnxOptions`) are a separate case — not config-bound — and are already settable, so leave them as-is unless asked.
