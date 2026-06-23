# KurrentDB Blazor UI — development guide

The admin UI lives in `src/KurrentDB/Components/**`. It is a **Blazor Server** app that runs **inside the
server process** and talks to the engine over the internal message bus — not over HTTP. These are the
conventions to follow when adding or changing UI features.

## The UI↔engine boundary: always a service

- Components **never** touch `IPublisher` or build messages directly. Every feature has a scoped
  `*Service` (registered in `Program.cs`) that is the *only* place the UI talks to the engine. This is
  enforced by `ComponentArchitectureTests` (a build-time test fails if any component injects `IPublisher`).
- A service wraps `IPublisher` + authorization, sends commands with `TcsEnvelope<T>` (in
  `KurrentDB.Core.Messaging`) and awaits the response, then maps results/denials to a domain exception the
  component handles. No HTTP calls, no manual `CallbackEnvelope`/`TaskCompletionSource`.
  - Single response type: `new TcsEnvelope<TResponse>()`, `publisher.Publish(...)`, `await env.Task.WaitAsync(ct)`.
  - Polymorphic responses: `TcsEnvelope<Message>()` + a `switch` over the response message types.
- The gRPC handlers in `src/KurrentDB.Core/Services/Transport/Grpc/` are the reference for which message,
  envelope, and response each operation uses — *and* for the `Operation` it authorizes against.

## Authorization

The transport layer (gRPC/HTTP) calls `IAuthorizationProvider.CheckAccessAsync(principal, Operation)` **before**
publishing; The UI must do the same — **each service must perform the same check itself, with the same `Operation`** (including
resource parameters like the `StreamId`, `$all`, `$$<stream>` metastreams).

- Services inject `IAuthorizationProvider` and authorize before publishing. Two shapes, both fine:
  - **Own a domain exception** → `if (!await authorizer.CheckAccessAsync(principal, op, ct)) throw new <Feature>Exception("Access denied.");`
  - **Surface the standard exception** → `await authorizer.EnsureAccessAsync(principal, op, ct);` (the
    throw-on-deny extension in `Components/Shared/AuthorizationProviderExtensions.cs`), caught by the component's generic handler.
- Build stream operations with `Components/Shared/UiOperations.cs` (`ReadStream`/`WriteStream`/`DeleteStream`/`ReadAll`) — don't hand-roll `new Operation(...).WithParameter(...)`.
- **Page gate:** `@attribute [Authorize(Policy = UiPolicies.X)]`. `Components/Shared/UiPolicies.cs` maps a named
  policy to an `Operation` (add a `const` + a `Register(...)` line); the bridge in `OperationAuthorization.cs`
  runs it via `CheckAccessAsync` before render (denied → `/ui/access-denied`).
- **Affordances:** `Disabled="@(!_can)"` where `_can = await authorizer.CheckAccessAsync(...)`, paired with a
  *generic* `MudTooltip` (don't name the permission — it depends on the policy). `<AuthorizeView>` for section visibility.
- **Never** hard-code roles (`[Authorize(Roles=…)]`, `IsInRole`) — gate on the `Operation` so access follows the configured policy.
- The `ClaimsPrincipal` comes from `[CascadingParameter] Task<AuthenticationState>` and is passed into every
  service call. Carrying the principal in a message is **not** authorization.

## Components

- **Markup in `.razor`; non-trivial C# in a `.razor.cs` code-behind partial** (inline `@code` only for trivial glue).
- **Business logic** (paging math, formatting, parsing, aggregation) goes in plain testable classes, not the component.
- **DI placement:** `@inject` in the `.razor` for services the *markup* uses; `[Inject]` in the code-behind for
  services only the code-behind uses (both compile to the same property on the partial class).
- **Render mode & providers are centralized** — `App.razor` sets `InteractiveServer` on `<Routes>`, and
  `MainLayout` hosts `MudThemeProvider` + the `MudPopover`/`MudDialog`/`MudSnackbar` providers once. Pages
  just need `@page` + the `[Authorize(Policy)]` gate; don't add per-page `@rendermode` or providers.
- A component opening a **live subscription** must skip the prerender pass: `if (!RendererInfo.IsInteractive) return;`.
- No `async void` handlers that do work — use `async Task`.
- Reuse `Components/Shared/StreamPageCursor` / `AllStreamPageCursor` for paging.
- Nav links go in `Components/Layout/NavMenu.razor`, shown to all authenticated users; the page's
  `[Authorize(Policy)]` is the single source of truth for access.

## Async return types

`ValueTask`/`ValueTask<T>` is the **default** for our own async methods (services and private helpers); await
each exactly once. Use `Task` only for: component **lifecycle overrides** (`OnInitializedAsync`, …);
**`EventCallback`/bound handlers** (`OnClick`, `@bind`, `ValueChanged`); **fire-and-forget** (`_ = X()`);
and anything passed as a `Func<Task>` delegate, used with `Task.Run`/`WhenAll`/`WhenAny`, stored, or awaited
more than once.

## Disposal & teardown

Implement `IDisposable`/`IAsyncDisposable` only if the component owns resources, and then release **all** of
them (and `seal` the component — a simple `Dispose` is not an extension point):
- `Timer` → `Dispose()`; live subscriptions → unsubscribe; event handlers (`x.Event += …`, incl.
  `Preferences.ThemeChanged`) → `-=`; `CancellationTokenSource` and `JsonDocument` → `using`.
- **Teardown race:** any fire-and-forget `InvokeAsync(StateHasChanged)` from a timer/subscription callback can
  run after disposal — guard with `catch (ObjectDisposedException)`. (Recurs in every polling page; polling is
  `Timer` + `Task.Run` with the refresh swallowing its own transient errors.)

## Theme

- All palette/colour lives in `UI/Theme/KurrentTheme.cs`: brand consts (`Primary` `#b75781` rose, `Secondary`
  `#631b3a` plum), `DarkPalette` + `LightPalette` (both branded; light reuses the brand accents), and
  `ChartPalette`/`DonutPalette(darkMode)`. `MainLayout` builds the `MudTheme` from these.
- `UI/Services/Preferences` (registered **scoped** — per-user) holds `DarkMode`, persisted in a non-HttpOnly
  `kurrentdb.theme` cookie: read server-side in `App.razor` to seed the theme flicker-free (and set the
  `theme-light`/`theme-dark` class on `<html>` so `app.css` paints the right background before render),
  written client-side on toggle via `window.kurrentTheme.save`. `MainLayout` binds `IsDarkMode` and the toggle.
- **Colour is sparing:** `MudButton`/`MudChip` are `Variant.Outlined`; don't set `Color` on buttons unless
  there's a strong semantic reason; `Primary` (rose) is for links/active items only.

## MudBlazor conventions & gotchas

- Dialogs: `MudDialog` + `MudForm` (not `EditForm`); submit calls `await _form.Validate()` then checks `_formValid`.
- Notifications: `ISnackbar.Add(...)` (bottom-right) for results; `DialogService.ShowMessageBox(...)` for confirmations.
- Data grids: `MudDataGrid`; clickable rows via `MudLink` in a `TemplateColumn` (not `RowClick`).
- `MudIconButton` has no `Title`/`TooltipText` (wrap in `MudTooltip` / use `aria-label`); `MudButton` has no `Form` param.
- JSON display: the recursive `Components/Tools/JsonTree` (+ `JsonNode`), theme-aware via `--json-*` CSS vars —
  not a JS widget. `Components/Streams/EventFormatting` pretty-prints JSON and falls back to a truncated raw preview for non-JSON.

## Testing

- `src/KurrentDB.Components.Tests` (xUnit + bUnit). Use `TestUtilities/MudBunit.NewContext(...)` for components
  that need MudBlazor services; a plain `BunitContext` for ones that don't.
- Each privileged service has a "recording-deny" auth test (a fake `IAuthorizationProvider` that denies, asserting
  the right `Operation` — with resource parameters — is checked before any publish).
- Keep `ComponentArchitectureTests` green: no component may inject `IPublisher`.
