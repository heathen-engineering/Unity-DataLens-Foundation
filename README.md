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

DataLens is a data-oriented simulation database. Instead of one C# object per entity, state lives in column-major native tables and logic is expressed as **Systems** — data-described operations the engine runs branchlessly across every row in parallel. It is built on five core types:

| Type | Purpose |
|------|---------|
| **`DataStore`** | A column-major table of fixed-width columns; entities are rows. Memcpy-speed typed cell access plus a validity bitmask for O(1) allocate/free with slot reuse. |
| **`Lens`** | The sole operator over stores. Owns a thread pool, runs Systems in dependency-ordered parallel waves, drives the tick/cadence scheduler, and refreshes views. |
| **`DataView`** | A read-only row-major snapshot (a copy, never an alias) of selected columns — safe to read on any thread while the Lens mutates the store. Bulk copy-out for rendering. |
| **`IrProgram` / `IrOp`** | A flat, pointer-free, serialisable program of System operations. Build once, execute many; the seam for networking and persistence. |
| **`Curve`** | A response-curve transform (linear / power / smoothstep / threshold) applied inside a System pass — the utility-AI scoring primitive. |

The following features are included:

- **Range-narrowed columns** — `Column.SmallestUnsigned`/`SmallestSigned` size each column to its value range (UInt8…UInt64, Int8…Int64, Float, Double); width is stride length, not a fixed `int`.
- **Parallel Systems** — `Set`/`Add`/`Sub`/`Mul`/`Min`/`Max` plus bitwise `And`/`Or`/`Xor`/`AndNot`, scalar or cross-column, with optional per-row predicates (including `HasAllBits`/`HasAnyBits`/`LacksBits` mask tests and mixed-type predicate columns).
- **Width/type-complete ops** — every column type (i8…u64, f32/f64) is bulk-operable through the IR, so narrow attributes pack to their true width.
- **Simulation LOD** — per-row LOD level + LOD bands so a tick can run a fidelity band; coarser tiers update less often.
- **Tick & cadence scheduler** — `AddScheduledProgram`/`AddScheduledView` run programs and refresh views at a period/phase; one `Tick` drives run-Systems → refresh-views.
- **Utility-AI substrate** — response-curve passes, counter-based reproducible noise (`RunFloatNoisePerturb`), and a multi-column `Argmax` select — score → perturb → pick, all as column ops.
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

### 1. Create a Store

```csharp
using Heathen.DataLens;

using var store = new DataStore(
    new[] { "PosX", "PosY", "VelX", "VelY" },
    new[] { DataLensValueType.Float, DataLensValueType.Float,
            DataLensValueType.Float, DataLensValueType.Float },
    preallocRows: 100_000);

// Entities are rows. Allocate from the validity bitmask (O(1), slots recycle).
ulong agent = store.AllocRow();
store.SetFloat(agent, col: 0, 12f); // PosX
```

### 2. Describe Systems and Build a Program

```csharp
using var lens = new Lens(); // owns a thread pool (0 = hardware concurrency)

using var step = new IrProgram();
step.Add(IrOp.FloatColumn(storeIndex: 0, targetCol: 0, SystemOp.Add, operandCol: 2)); // PosX += VelX
step.Add(IrOp.FloatColumn(storeIndex: 0, targetCol: 1, SystemOp.Add, operandCol: 3)); // PosY += VelY

// Run across every live row, in parallel, branchlessly.
lens.Execute(step, store);
```

### 3. Schedule a Tick Loop + Read via a View

```csharp
var posView = new DataView(new ulong[] { 0, 1 }); // PosX, PosY snapshot

lens.AddScheduledProgram(step, period: 1);
lens.AddScheduledView(posView, storeIndex: 0, period: 1);

// Each frame:
lens.Tick(store);                 // run due Systems, then refresh due views
var buffer = new float[posView.RowCount * posView.ColumnCount];
posView.CopyFloats(buffer);       // bulk read-out for rendering
```

### 4. Utility-AI Scoring (curves + noise + argmax)

```csharp
// Score a column through a falling response curve (e.g. "want healing as HP drops")
lens.RunFloatCurved(store, targetCol: scoreCol, SystemOp.Set, operandCol: hpCol,
    Curve.Linear(min: 0f, max: 100f, slope: -1f, intercept: 1f));

// Perturb by per-agent variance × reproducible noise, then pick the best of K score columns
lens.RunFloatNoisePerturb(store, scoreCol, SystemOp.Add, operandCol: varianceCol,
    noiseLo: 0f, noiseHi: 1f, seed: 1234, tick: lens.CurrentTick);
lens.RunFloatArgmax(store, choiceCol, new ulong[] { scoreA, scoreB, scoreC });
```

-----

## API Reference

### `DataStore`

| Member | Description |
|--------|-------------|
| `new DataStore(names, types, preallocRows)` | Create a fixed-capacity column table |
| `AllocRow()` / `FreeRow(row)` | Allocate / free a row via the validity bitmask (`InvalidRow` when full) |
| `LiveCount` / `RowCount` / `ColumnCount` / `RowStride` | Counts and layout |
| `SetFloat`/`SetInt`/`SetDouble(row, col, v)` | Typed cell write (stride-aware) |
| `TryGetFloat`/`TryGetInt`/`TryGetDouble(row, col, out v)` | Typed cell read |
| `SetValid(row, valid)` / `IsValid(row)` | Liveness bit |
| `SetLod(row, level)` / `GetLod(row)` | Per-row simulation LOD |
| `RunFloat`/`RunInt[Column]` | Run one System over this store (scalar/cross-column, optional predicate) |

### `Lens`

| Member | Description |
|--------|-------------|
| `new Lens(threadCount = 0)` | Create the orchestrator (0 = hardware concurrency) |
| `Execute(program, params stores)` | Run an `IrProgram` across stores in dependency-ordered parallel waves |
| `RunBatch(params systems)` / `RunBatchInLodBand` | Run an array of `SystemDesc` |
| `RunFloatCurved` / `RunIntCurved` | System pass with a `Curve` transform |
| `RunFloatNoise` / `RunFloatNoisePerturb` (+ `Int`) | Counter-based reproducible noise fill / perturb |
| `RunFloatArgmax` / `RunIntArgmax` | Write the index of the max across K score columns |
| `RunFloatWhereInt` / `RunIntWhereFloat` | Mixed-type predicate System |
| `AddScheduledProgram` / `AddScheduledView(InLodBand)` | Register periodic programs / view refreshes |
| `Tick(params stores)` | Advance the clock: run due Systems, then refresh due views |
| `RefreshView(view, store)` (+ `InLodBand`) | Parallel re-materialise a view now |
| `CurrentTick` / `ResetTick` | Scheduler clock |

### `DataView`

| Member | Description |
|--------|-------------|
| `new DataView(sourceColumns)` | A read-only snapshot over selected columns |
| `Refresh(store)` / `RefreshInLodBand` | Re-materialise from the store's live rows |
| `RowCount` / `ColumnCount` / `RowStride` | Snapshot layout |
| `SourceRow(viewRow)` | Map a view row back to its store row |
| `TryGetFloat`/`TryGetInt`/`TryGetDouble` | Typed cell read |
| `CopyFloats(dst)` / `CopyInts(dst)` | Bulk one-shot read-out (for rendering) |
| `DataPointer` / `ByteSize` | Contiguous raw buffer access |

### `IrProgram` / `IrOp`

| Member | Description |
|--------|-------------|
| `IrOp.Float`/`Int(store, col, op, operand)` | Scalar System op |
| `IrOp.FloatColumn`/`IntColumn(...)` | Cross-column System op |
| `IrOp.Typed`/`TypedColumn(store, elemType, ...)` | Any-width (i8…u64/f64) System op |
| `IrOp.CurvedColumn(...)` / `.WithCurve(curve)` | Response-curve transform |
| `.WithPredicate(col, cmp, threshold)` / `.WithLodBand(min, max)` | Per-op predicate / LOD band |
| `program.Add(op)` / `Count` | Build the program |
| `Serialize()` / `Deserialize(bytes)` | Round-trip the IR (networking / persistence seam) |

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
| `Heathen.DataLens` | All runtime types: `DataStore`, `Lens`, `DataView`, `IrProgram`/`IrOp`, `SystemDesc`, `Curve`, `Column`, and the enums |
