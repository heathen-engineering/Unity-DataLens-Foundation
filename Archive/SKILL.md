---
name: heathen-datalens-unity-foundation
description: Orientation for an agent integrating DataLens (a cache-aware, column-oriented in-memory simulation store) into a Unity project via the DataLens Foundation package.
---

# DataLens Foundation (Unity)

A data-oriented simulation database for Unity. Entity state lives in column-major native
tables (bit-packed, cache-line-aligned), addressed by GameplayTag, and updated via branchless
parallel column operations instead of per-object heap access — built for large-scale, emergent
actor/entity systems (target: hundreds of thousands to millions of actors updating at
sub-millisecond cost). This repo (`com.heathen.datalensfoundation`) is the managed Unity binding
(P/Invoke + facade) over the engine-agnostic native DataLens Core.

## Tier

**Foundation only — there is no DataLens Toolkit tier.** This is by design, not a gap: DataLens
is meant to be consumed directly (hand-written schemas) or through a higher-layer product's own
Toolkit (e.g. HATE), not through a DataLens-specific visual authoring layer. If a DataLens
Toolkit is ever built, expect it elsewhere in the SourceRepo Unity tree, not in this repo.

Also note: DataLens has a **native Core layer** (`github.com/heathen-engineering/DataLens`,
engine-agnostic C++17, stable C ABI in `c_api.h`) that this package binds against. The prebuilt
`libdatalens.so`/`datalens.dll` binaries ship vendored inside this package — most users never
touch the Core directly or need to build it. Only rebuild the Core if you're changing it or
retargeting a platform; see "Rebuilding the native library" in `README.md`.

## Up

[`github.com/heathen-engineering/SourceRepo/SKILL.md`](https://github.com/heathen-engineering/SourceRepo/blob/main/SKILL.md)
(ecosystem guide — no local engine-level `SKILL.md` to link to since this is a standalone repo).

## Key namespaces / entry points

Namespace: `Heathen.DataLens` (all public consumer types). Internal-only (per the package's own
"Coding Law 4" — the `Lens` owns them, consumers never touch them directly): `DataStore`, the
non-generic snapshot view, `IrProgram`, `SystemDesc`.

| Type | Purpose |
| :--- | :--- |
| `DataLensSchema` / `DataStoreSchema` / `DataColumn` | Declare the database. A store groups index-aligned columns; `DataColumn.Of<T>(tag)` (compile-time type) / `DataColumn.OfType(tag, DataLensValueType)` (runtime/data-driven). |
| `Lens` | The sole operator. `new Lens(schema, threadCount = 0)` creates + owns the native stores and a thread pool. Runs tag-addressed Systems (`RunSystem`/`RunSystemColumn`), opens Views (`View<TRow>()` / `View(DataLensFrom, columnTags)`), drives the tick scheduler (`Tick()`/`CurrentTick`), and exposes replication (`Revision`/`BumpRevision`/`SetRevision`/`Snapshot`/`CollectDelta`/`ApplyPayload`). |
| `DataView<TRow>` | Typed read/write View over an `unmanaged` row struct (`IDataLensViewRecord`, fields tagged `[DataLensColumn(tag)]`); zero-copy `Span<TRow>` (`Rows`) when widths match. `AddRow()`, `GetState`/`SetState(row, ViewRowState)`, `Refresh()`/`Commit()`. |
| `DataLensView` | Dynamic, column-addressed View — `Get<T>`/`Set<T>(row, columnTag)` — for data-driven consumers (e.g. codegen). |
| `DataLensFrom` | View topology builder: `new DataLensFrom(primeStore)` + `.Dereference(into, via)` / `.Aligned(into)` joins + `.Where(...)` filters. |
| `DataLensFilter` / `DataLensPredicate` | Serialisable boolean predicate tree (`Eq`/`Less`/`Greater`/`InRange`/`HasAnyBits`/`And`/`Or`/`Not`), compiled to the Core's scope program. |
| `Curve` | Response-curve transform: `Identity`, `Linear`, `Power`, `Smoothstep`, `Threshold`. |
| `SystemOp` | `Set`, `Add`, `Sub`, `Mul`, `Min`, `Max`, `And`, `Or`, `Xor`, `AndNot`. |
| `CompareOp` | `Always`, `Eq`, `Ne`, `Lt`, `Le`, `Gt`, `Ge`, `HasAllBits`, `HasAnyBits`, `LacksBits`. |
| `DataLensValueType` / `Column` | `Bool`…`UInt64`/`Float`/`Double`/`Guid`; `Column.SmallestUnsigned(max)` / `SmallestSigned(min, max)` range-narrowing helpers. |

Verified against `com.heathen.datalensfoundation/Runtime/*.cs` (real source), consistent with
this repo's own `README.md`.

## Dependencies

- **`com.heathen.gameplaytags`** (pinned `1.0.10` in `package.json`) — hard UPM dependency.
  Column/store identity is a GameplayTag `u64` hash, not a string/enum; DataLens is a direct
  consumer of GameplayTags Foundation's interval-encoded hierarchy.
- No other managed dependencies. The native libraries (`libdatalens.so` Linux, `datalens.dll`
  Windows x86_64, MinGW-w64 cross-build) are vendored inside the package — no build step and no
  third-party-dependency-guarding pattern needed to use it as-is. macOS (`.dylib`) is not yet
  built.

## Common tasks

- **Declare a schema and build a `Lens`**: `new DataLensSchema().Add(new DataStoreSchema(tag,
  capacity, DataColumn.Of<float>(tag), ...))`, then `new Lens(schema)`. See `README.md` §"Declare
  a schema and build a Lens".
- **Run a columnar update (branchless, parallel)**: `lens.RunSystemColumn(store, col, SystemOp.Add,
  operandCol)` or `lens.RunSystem(store, col, op, operand[, compareCol, cmp, threshold])` for a
  scalar op with an optional predicate.
- **Read/write typed rows**: define a row struct implementing `IDataLensViewRecord` with
  `[DataLensColumn(tag)]` fields and a static `From()`, then `lens.View<TRow>()` →
  `view.Rows`/`AddRow()`/`SetState()`/`Commit()`/`Refresh()`.
- **Read/write by tag without a compile-time struct**: `lens.View(DataLensFrom, columnTags)` →
  `DataLensView.Get<T>`/`Set<T>(row, tag)`.
- **Join across stores or filter rows a View sees**: `DataLensFrom.Dereference(into, via)` /
  `.Aligned(into)`, `.Where(...)`, or build a `DataLensFilter` tree directly.
- **Replicate a store over your own netcode**: `lens.BumpRevision(store)` each tick,
  `lens.CollectDelta(store, sinceRevision)` for per-tick deltas, `lens.Snapshot(store)` for a
  late-join baseline, `lens.ApplyPayload(store, payload)` on the receiving side. DataLens supplies
  the wire primitives only — no socket, no authority model, no topology. See `README.md` §"Replicate
  a store" for a full host/client example.
- **Run tiered-fidelity simulation (LOD)**: per-row LOD level/bands plus
  `AddScheduledProgram`/`AddScheduledView` cadence scheduling — see `README.md`'s Simulation LOD
  and tick-scheduler bullets before writing new scheduling code, verify current signatures against
  `Runtime/*.cs` first.
- **Rebuild the native library from Core** (rarely needed): `./build-native.sh [path-to-DataLens/Core]`
  at the repo root.

## Where full usage docs live

No public Knowledge Base article for DataLens was found at the time of writing — don't assume one
exists. `README.md` at this repo's root is the fullest usage reference currently available
(schema/Lens/View/replication walkthroughs with real code samples); this `SKILL.md` summarizes it
for offline agent use but the README has more prose/context if needed.

## Version

`com.heathen.datalensfoundation/package.json` (`version` field, currently `0.1.0`) +
`com.heathen.datalensfoundation/CHANGELOG.md`. Check both directly before citing a version —
this package is early-stage and the version may move.
