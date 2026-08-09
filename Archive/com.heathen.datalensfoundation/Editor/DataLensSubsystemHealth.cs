using System;
using System.Collections.Generic;
using Heathen.Editor;

namespace Heathen.DataLens.Editor
{
    /// <summary>
    /// Reports to the Game Framework when the native DataLens library is missing or its C ABI does not match the
    /// binding, so the <see cref="DataLensSubsystem"/> shows an attention chip on Project ▸ Subsystems (and in
    /// the play-mode guard / Scene-view overlay). Also supplies the subsystem's documentation link. DataLens has
    /// no project configuration, so it exposes no settings page.
    /// </summary>
    public sealed class DataLensSubsystemHealth : ISubsystemHealth, ISubsystemDocumentation
    {
        public Type SubsystemType => typeof(DataLensSubsystem);

        public string DocumentationUrl => "https://heathen.group/kb/datalens-welcome/";

        public IEnumerable<SubsystemIssue> GetIssues()
        {
            if (!DataLensSubsystem.NativeAvailable)
                yield return new SubsystemIssue(
                    SubsystemHealthSeverity.Error,
                    "The native 'datalens' library was not found. Ensure the plugin ships for your target platform.");
            else if (DataLensSubsystem.NativeAbiVersion != DataLensSubsystem.AbiVersion)
                yield return new SubsystemIssue(
                    SubsystemHealthSeverity.Error,
                    $"Native ABI mismatch: the binding expects {DataLensSubsystem.AbiVersion} but the library " +
                    $"reports {DataLensSubsystem.NativeAbiVersion}. Update the DataLens native plugin.");
        }
    }
}
