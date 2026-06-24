using System.Runtime.CompilerServices;

// The DataStore handle's constructor is internal (gameplay code works through a Lens + DataViews). The
// Foundation's own test and demo assemblies are friends so they can build stores for low-level coverage.
[assembly: InternalsVisibleTo("Heathen.DataLens.ValidationTests")]
[assembly: InternalsVisibleTo("Heathen.DataLens.Demo")]
