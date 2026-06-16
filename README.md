# Unity DataLens Foundation

Unity binding over the engine-agnostic native [DataLens](https://github.com/heathen-engineering)
core: a cache-aware, column-oriented in-memory simulation store. Open source (Apache 2.0).

> **Status: A1 walking-skeleton slice.** This is the thin vertical slice that proves the Unity
> (managed) <-> native (C/C++) boundary end to end: a minimal C ABI over the native `DataStore`,
> a C# P/Invoke layer, and a managed `DataStore` facade. The full world / Lens / view surface
> arrives as the native core grows (phases A2-A7). HATE builds on this later.

## Package
`com.heathen.datalensfoundation` — consumed by the Toolkit project (ToolkitSource) via a local
`file:` reference in `Packages/manifest.json`, like the other Heathen Foundations.

```
com.heathen.datalensfoundation/
  package.json
  Runtime/
    Heathen.DataLens.Foundation.asmdef
    DataLensValueType.cs              enum mirror of native DataLensValueType
    DataLensNative.cs                 internal P/Invoke surface (datalens C ABI)
    DataStore.cs                      managed facade (IDisposable handle)
    Plugins/Linux/x86_64/
      libdatalens.so                  vendored native library (Linux x86_64)
```

## The native library
The `.so` is built from `DataLens/Core` (CMake, Linux-first). To rebuild and re-vendor it:
```sh
./build-native.sh                 # defaults to ~/Dev/GitHub/DataLens/Core
./build-native.sh /path/to/DataLens/Core
```
Linux x86_64 only for now; Windows/macOS slices come later.

## Usage
```csharp
using Heathen.DataLens;

using var store = new DataStore(
    new[] { "Health", "Team", "Stamina" },
    new[] { DataLensValueType.Float, DataLensValueType.Int32, DataLensValueType.Double },
    preallocRows: 1024);

store.SetFloat(row: 0, col: 0, 100f);
store.TryGetFloat(0, 0, out float hp);
```

## Validation
The ToolkitSource project contains an EditMode test suite (`Heathen.DataLens.ValidationTests`)
that creates a store and round-trips cells through the native library, confirming the binding
loads and works in the Unity editor.
