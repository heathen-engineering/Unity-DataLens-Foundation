using System;
using System.Collections.Generic;
using Heathen;
using UnityEngine;

namespace Heathen.DataLens
{
    /// <summary>
    /// Global framework subsystem for DataLens. Unlike the other Foundations, DataLens owns no global mutable
    /// state: a <see cref="Lens"/> (and its stores/views) is created and owned by the consumer (typically one
    /// per HATE world), so there is no session registry to reset. What is global is the native library:
    /// this subsystem verifies at framework boot (<see cref="SubsystemScope.Global"/> subsystems initialise at
    /// <c>RuntimeInitializeLoadType.SubsystemRegistration</c>) that the native <c>datalens</c> library is
    /// present and its C ABI matches the version this binding was built against, failing fast with a clear
    /// message instead of a cryptic <see cref="DllNotFoundException"/> deep inside gameplay. It also surfaces
    /// native/diagnostic state to the Subsystem Debug window via <see cref="ISubsystemDebug"/>.
    /// </summary>
    [Subsystem(SubsystemScope.Global)]
    public sealed class DataLensSubsystem : Subsystem, ISubsystemDebug
    {
        /// <summary>
        /// The native C ABI version this binding was built against (mirrors <c>dl_abi_version()</c> in
        /// <c>datalens/c_api.h</c>). A loaded library reporting a different value is incompatible.
        /// </summary>
        public const int AbiVersion = 1;

        /// <summary>The number of live (undisposed) <see cref="Lens"/> instances. Diagnostic; maintained by <see cref="Lens"/>.</summary>
        public static int LiveLensCount { get; private set; }

        internal static void RegisterLens()   => LiveLensCount++;
        internal static void UnregisterLens() { if (LiveLensCount > 0) LiveLensCount--; }

        // Cached one-time probe of the native library (a P/Invoke that may throw if the .so/.dll is absent).
        private static bool _probed;
        private static bool _nativeAvailable;
        private static int  _nativeAbiVersion = -1;

        /// <summary>Whether the native <c>datalens</c> library loaded successfully.</summary>
        public static bool NativeAvailable { get { Probe(); return _nativeAvailable; } }

        /// <summary>The C ABI version the loaded library reports, or -1 when it could not be queried.</summary>
        public static int NativeAbiVersion { get { Probe(); return _nativeAbiVersion; } }

        /// <summary>Whether the native library is present and its ABI matches <see cref="AbiVersion"/>.</summary>
        public static bool AbiCompatible => NativeAvailable && _nativeAbiVersion == AbiVersion;

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                _nativeAbiVersion = DataLensNative.dl_abi_version();
                _nativeAvailable  = true;
            }
            catch (DllNotFoundException)        { _nativeAvailable = false; }
            catch (EntryPointNotFoundException) { _nativeAvailable = true; _nativeAbiVersion = -1; }
        }

        /// <summary>Verifies the native library at boot, logging a clear error on absence or ABI mismatch.</summary>
        protected override void Initialize()
        {
            Probe();
            if (!_nativeAvailable)
                Debug.LogError(
                    "[DataLens] The native 'datalens' library could not be loaded. Ensure the plugin ships for " +
                    "the target platform (Runtime/Plugins/<platform>). DataLens features will not function.");
            else if (_nativeAbiVersion != AbiVersion)
                Debug.LogError(
                    $"[DataLens] Native ABI mismatch: this binding expects ABI {AbiVersion} but the loaded " +
                    $"library reports {_nativeAbiVersion}. Update the DataLens native plugin to match.");
        }

        /// <inheritdoc/>
        public IEnumerable<(string label, string value)> GetDebugInfo()
        {
            Probe();
            yield return ("Native library", _nativeAvailable ? "loaded" : "NOT FOUND");
            yield return ("ABI version",    _nativeAvailable ? $"{_nativeAbiVersion} (expects {AbiVersion})"
                                                             : $"expects {AbiVersion}");
            yield return ("Hardware concurrency", Environment.ProcessorCount.ToString());
            yield return ("Active lenses", LiveLensCount.ToString());
        }
    }
}
