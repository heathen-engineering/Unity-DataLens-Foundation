# DataLens Foundation

![License](https://img.shields.io/badge/License-Apache_2.0-blue?style=flat-square)
![Maintained](https://img.shields.io/badge/Maintained%3F-yes-green?style=flat-square)
![Unity](https://img.shields.io/badge/Unity-2021.3%20%2B-black?style=flat-square&logo=unity&logoColor=white)
[![Native](https://img.shields.io/badge/Native-C%2B%2B17%20core-lightgrey?style=flat-square)](https://github.com/heathen-engineering/DataLens)

A cache-aware, column-oriented in-memory simulation store for Unity. Entities are rows, attributes are bit-packed columns, and updates run as branchless parallel passes over native memory — millions of agents simulated per frame with zero per-entity GC objects.

-----

## ⚙ Built on the DataLens Core

This package is the Unity binding over the engine-agnostic native [**DataLens Core**](https://github.com/heathen-engineering/DataLens) (C++17, Linux/Windows). The Core is where portability lives; each engine ships its own thin Foundation over it. Same substrate powers [HATE](https://github.com/heathen-engineering/Unity-Heathen-Attribute-gpTag-Engine-Foundation) on top.

-----

## Become a GitHub Sponsor
[![Discord](https://img.shields.io/badge/Discord--1877F2?style=social&logo=discord)](https://discord.gg/6X3xrRc)
[![GitHub followers](https://img.shields.io/github/followers/heathen-engineering?style=social)](https://github.com/heathen-engineering?tab=followers)  
Support Heathen by becoming a [GitHub Sponsor](https://github.com/sponsors/heathen-engineering). Sponsorship directly funds the development and maintenance of free tools like this, as well as our game development [Knowledge Base](https://heathen.group/) and community on [Discord](https://discord.gg/6X3xrRc).

Sponsors also get access to our private SourceRepo, which includes developer tools for O3DE, Unreal, Unity, and Godot.  
Learn more or explore other ways to support @ [heathen.group/kb](https://heathen.group/kb/do-more/)

-----

## What it does

DataLens is a data-oriented simulation database. Instead of one C# object per entity, state lives in column-major native tables (GameplayTag-addressed), and a consumer works through **Views** and tag-addressed **Systems** — never touching a store directly (the Lens is the sole operator). The public surface:

| Type | Purpose |
|------|---------|
| **`DataLensSchema` / `DataStoreSchema` / `DataColumn`** | Declare the database. A store groups index-aligned columns; a column is a `(GameplayTag id, fixed stride, default)`. Build columns with `DataColumn.Of<T>(tag)` or `DataColumn.OfType(tag, valueType)`. |
| **`Lens`** | The sole operator. Built from a schema (it creates + owns the native stores), owns a thread pool, runs tag-addressed **Systems** (`RunSystem`/`RunSystemColumn`), opens **Views**, and drives the tick scheduler. |
| **`DataView<TRow>`** | A typed read/write View: an `unmanaged` row struct with `[DataLensColumn]` fields + a static `From()` topology (prime store + dereference joins + a predicate filter). Zero-copy `Span<TRow>` when widths match, else marshalled; edit in place + `Commit`. |
| **`DataLensView`** | The dynamic, column-addressed read/write View (no compile-time struct): `Get<T>`/`Set<T>` cells by column tag — for data-driven consumers (e.g. a codegen'd engine like HATE). |
| **`DataLensFilter` / `DataLensPredicate`** | A serialisable boolean predicate tree (`Eq`/`Less`/`InRange`/`And`/`Or`/`Not`) compiled to the Core's scope program; scopes which rows a View hydrates. |
| **`Curve`** | A response-curve transform (linear / power / smoothstep / threshold). |

> The low-level store / System / IR primitives (`DataStore`, the non-generic snapshot view, `IrProgram`, `SystemDesc`) are **internal** — the Lens owns them (Coding Law 4); consumers ride Views and `RunSystem`.

The following features are included:

- **Range-narrowed columns** — `Column.SmallestUnsigned`/`SmallestSigned` size each column to its value range (UInt8…UInt64, Int8…Int64, Float, Double); width is stride length, not a fixed `int`.
- **Parallel Systems** — `Set`/`Add`/`Sub`/`Mul`/`Min`/`Max` plus bitwise `And`/`Or`/`Xor`/`AndNot`, scalar or cross-column, with optional per-row predicates (including `HasAllBits`/`HasAnyBits`/`LacksBits` mask tests and mixed-type predicate columns).
- **Width/type-complete ops** — every column type (i8…u64, f32/f64) is bulk-operable through the IR, so narrow attributes pack to their true width.
- **Simulation LOD** — per-row LOD level + LOD bands so a tick can run a fidelity band; coarser tiers update less often.
- **Tick & cadence scheduler** — `AddScheduledProgram`/`AddScheduledView` run programs and refresh views at a period/phase; one `Tick` drives run-Systems → refresh-views.
- **Utility-AI substrate** — response-curve passes, counter-based reproducible noise (`RunFloatNoisePerturb`), and a multi-column `Argmax` select — score → perturb → pick, all as column ops.
- **Replication enablement (provider, not a networking system)** — per-store revision counter (`Revision`/`BumpRevision`/`SetRevision`), a late-join `Snapshot(store, scope)`, a `CollectDelta(store, sinceRevision, scope)` (column-level diffs, endian-free), and `ApplyPayload(store, payload)` — the pointer-free wire primitives a netcode stack (Mirror/NGO/FishNet/Unreal) *consumes*. DataLens opens no socket, dictates no topology, and enforces no authority; it exposes the hooks and gets out of the way. Rollback = snapshot-then-apply; interest management = the scope predicate.
- **Native, no GC** — all hot state is in the native library; the managed layer is thin P/Invoke facades. Linux **and** Windows x86_64 binaries are vendored in-package.

---

## Requirements

- Unity **2021.3** or compatible
- Linux or Windows, **x86_64** (the vendored native plugins; macOS pending)
- No managed package dependencies

---

## Installation

### Via Unity Package Manager (UPM)

1. In Unity, go to `Window > Package Manager`.
2. Click **+** > **Add package from git URL**.
3. Enter:
   ```
   https://github.com/heathen-engineering/Unity-DataLens-Foundation.git?path=/com.heathen.datalensfoundation
   ```

The native libraries (`libdatalens.so` / `datalens.dll`) ship inside the package — no build step required to use it.

-----

## Setup & Workflow

### 1. Declare a schema and build a Lens

```csharp
using Heathen.DataLens;

var schema = new DataLensSchema()
    .Add(new DataStoreSchema("Game.Movement", capacity: 100_000,
        DataColumn.Of<float>("Game.Movement.PosX"),
        DataColumn.Of<float>("Game.Movement.PosY"),
        DataColumn.Of<float>("Game.Movement.VelX"),
        DataColumn.Of<float>("Game.Movement.VelY")));

using var lens = new Lens(schema); // creates + owns the native stores; owns a thread pool
```

### 2. Run a System (columnar, branchless, parallel)

```csharp
// PosX += VelX across every live row of the store — tag-addressed, no store handle needed.
lens.RunSystemColumn("Game.Movement", "Game.Movement.PosX", SystemOp.Add, "Game.Movement.VelX");
lens.RunSystemColumn("Game.Movement", "Game.Movement.PosY", SystemOp.Add, "Game.Movement.VelY");
```

### 3. Read / write through a typed View

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Mover : IDataLensViewRecord
{
    [DataLensColumn("Game.Movement.PosX")] public float X;
    [DataLensColumn("Game.Movement.PosY")] public float Y;
    public static DataLensFrom From() => new DataLensFrom("Game.Movement");
}

using var view = lens.View<Mover>();
int r = view.AddRow();                          // insert
view.Rows[r] = new Mover { X = 12f, Y = 0f };
view.SetState(r, ViewRowState.New);
view.Commit();                                  // write-back to the store

view.Refresh();                                 // re-hydrate the snapshot
foreach (ref readonly var m in view.Rows) { /* zero-copy read of PosX/PosY */ }
```

A cross-store Entity System adds dereference joins and a filter to `From()`
(`new DataLensFrom("...Catalog").Dereference(into, via).Where(p => p.InRange(...))`); a data-driven consumer
uses the dynamic `lens.View(from, columnTags)` and `Get<T>`/`Set<T>` by tag instead of a row struct.

### 4. Replicate a store (hooks for your netcode, no transport of our own)

DataLens ships the wire primitives; **your** netcode stack (Mirror, NGO, FishNet, Unreal, custom) owns the
socket, authority, and topology. The revision counter, a snapshot, and per-tick deltas are all you need.

```csharp
const string Store = "Game.Movement";

// ── Host: advance the revision each network tick, then collect + send the delta ──
lens.BumpRevision(Store);                       // stamp this tick's writes with a new revision
// ... run Systems / commit Views for the tick (they mark changed columns dirty) ...
byte[] delta = lens.CollectDelta(Store, sinceRevision: lastAckedByClient);
transport.Send(delta);                          // YOUR channel: RPC, NetworkVariable, raw socket...

// ── Late join / first sync: send a full baseline instead of a delta ──
byte[] baseline = lens.Snapshot(Store);         // whole store
transport.SendTo(newPeer, baseline);

// Interest management: scope either call to a row-index bitmask (e.g. an entity's rows) so a peer
// only receives what it can see — this is exactly what HATE's CollectEntityScope hands you.
byte[] scoped = lens.CollectDelta(Store, sinceRevision, scope: visibleRowsBitmask);

// ── Client: apply whatever arrived (snapshot or delta) ──
lens.ApplyPayload(Store, receivedBytes);        // reconciles the local store to the payload
ulong now = lens.Revision(Store);               // ack this back to the host

// Rollback (client prediction): snapshot a scope before a speculative action, restore on misprediction.
byte[] preAction = lens.Snapshot(Store, scope: predictedRows);
// ... if mispredicted:
lens.ApplyPayload(Store, preAction);
```

Payloads are pointer-free and endianness-free (safe cross-platform / cross-engine), and deltas are column-level
(only changed cells travel). DataLens never opens a socket or decides authority — see `DataLens-Spec.md` §10.

-----

## API Reference

### `DataLensSchema` / `DataStoreSchema` / `DataColumn`

| Member | Description |
|--------|-------------|
| `new DataLensSchema().Add(DataStoreSchema)` | Declare the database (chainable) |
| `new DataStoreSchema(tag, capacity, params DataColumn[])` | A store: index-aligned columns + a row capacity |
| `DataColumn.Of<T>(tag[, defaultValue])` | A fixed-width column from a compile-time type |
| `DataColumn.OfType(tag, DataLensValueType[, defaultBytes])` | A column from a runtime value type (data-driven) |

### `Lens`

| Member | Description |
|--------|-------------|
| `new Lens(schema, threadCount = 0)` | Build from a schema; creates + owns the native stores + a thread pool |
| `View<TRow>(weight = 0)` | Open a typed read/write View (record struct) |
| `View(DataLensFrom, columnTags, readOnly = null, weight = 0)` | Open a dynamic column-addressed View |
| `RunSystem(store, col, op, operand[, compareCol, cmp, threshold])` | Tag-addressed scalar Store System (optional predicate) |
| `RunSystemColumn(store, col, op, operandCol)` | Tag-addressed cross-column Store System |
| `Commit()` | Weight-ordered write-back of all registered Views (heavier weight wins per cell) |
| `Tick()` / `CurrentTick` / `ResetTick` | Advance / read the scheduler clock |
| `Revision(store)` / `BumpRevision(store)` / `SetRevision(store, r)` | Per-store replication revision counter |
| `Snapshot(store, scope = null)` | Serialise a store (or a row-scoped subset) to a portable byte payload (late-join baseline) |
| `CollectDelta(store, sinceRevision, scope = null)` | Serialise the column-level changes since a revision (per-tick delta) |
| `ApplyPayload(store, payload)` | Apply a received snapshot/delta payload to a store |

### `DataView<TRow>` / `DataLensView`

| Member | Description |
|--------|-------------|
| `Rows` | Typed `Span<TRow>` window (zero-copy or marshalled) — `DataView<TRow>` only |
| `Get<T>`/`Set<T>(row, columnTag)` | Dynamic cell read/write by tag — `DataLensView` only |
| `AddRow()` | Append a New row (Insert) |
| `GetState`/`SetState(row, ViewRowState)` | Per-row change flag (`Unchanged`/`Modified`/`New`/`Removed`) |
| `Refresh()` / `Commit()` | Re-hydrate the snapshot / write changed rows back |
| `RowCount` / `SourceRow(viewRow)` / `SourceJoinRow(viewRow, join)` | Layout + map a view row to its source store row(s) |

### `DataLensFrom` / `DataLensFilter` (View topology + filters)

| Member | Description |
|--------|-------------|
| `new DataLensFrom(primeStore)` | The base store a View reads from |
| `.Dereference(into, via[, absentSentinel])` / `.Aligned(into)` | Glue in another store (index dereference / row-aligned) |
| `.Where(tag, op, value)` / `.WhereInRange(tag, lo, hi)` / `.Where(p => …)` | Filters (compiled to a serialisable predicate tree) |
| `new DataLensFilter().Eq/Less/Greater/InRange/HasAnyBits/And/Or/Not(…)` | Build a predicate tree |

### Enums & Helpers

| Type | Values / Members |
|------|------------------|
| `SystemOp` | `Set`, `Add`, `Sub`, `Mul`, `Min`, `Max`, `And`, `Or`, `Xor`, `AndNot` |
| `CompareOp` | `Always`, `Eq`, `Ne`, `Lt`, `Le`, `Gt`, `Ge`, `HasAllBits`, `HasAnyBits`, `LacksBits` |
| `DataLensValueType` | `Bool`, `Int8`…`UInt64`, `Float`, `Double`, `Guid` |
| `CurveType` | `Linear`, `Power`, `Smoothstep`, `Threshold` |
| `Curve` | `Identity`, `Linear(...)`, `Power(...)`, `Smoothstep(...)`, `Threshold(...)` factories |
| `Column` | `SmallestUnsigned(max)`, `SmallestSigned(min, max)` range-narrowing |

-----

## Rebuilding the native library

The binaries are pre-vendored, so most users never rebuild. To rebuild from the [DataLens Core](https://github.com/heathen-engineering/DataLens) and re-vendor into this package:

```sh
./build-native.sh                 # defaults to ~/Dev/GitHub/DataLens/Core
./build-native.sh /path/to/DataLens/Core
```

Linux is built natively; Windows is a MinGW-w64 cross-build (see the Core `README.md`). macOS (`.dylib`) is pending.

-----

## Namespaces

| Namespace | Contents |
|-----------|----------|
| `Heathen.DataLens` | The public consumer types: `DataLensSchema`/`DataStoreSchema`/`DataColumn`, `Lens`, `DataView<TRow>` (+ `IDataLensViewRecord`/`[DataLensColumn]`), `DataLensView`, `DataLensFrom`/`DataLensFilter`/`DataLensPredicate`, `Curve`, `Column`, and the enums. The store/System/IR primitives are internal. |
