# src/SharpCpp.Std/

## Responsibility

`SharpCpp.Std` is the shared managed wrapper assembly for supported C++ standard-library container families. Public wrappers live in `Std.*`; generated modules provide their native operations through contracts in `Std.Infrastructure`.

## Structure

- `Std.cs` — string, sequence, fixed-array, and dictionary wrappers.
- `Infrastructure/ProviderContracts.cs` — provider contracts generated modules implement.
- `Infrastructure/ProviderRegistry.cs` — thin `Std` facade over the generic registry in `SharpCpp.Support`.
- `AssemblyMarker.cs` — assembly identity token.

The assembly contains no native-library-specific implementation and depends only on `SharpCpp.Support`.
