# rbfx/SharpCpp.Ea/

## Responsibility

`SharpCpp.Ea` is the rbfx-owned shared managed wrapper assembly for EASTL container families. Public wrappers live in `Ea.*`; generated rbfx modules provide native operations through contracts in `Ea.Infrastructure`.

## Structure

- `Ea.cs` — string, sequence, fixed/dynamic-array, and dictionary wrappers.
- `Infrastructure/ProviderContracts.cs` — EASTL provider contracts generated modules implement.
- `Infrastructure/ProviderRegistry.cs` — thin `Ea` facade over the generic registry in `SharpCpp.Support`.
- `AssemblyMarker.cs` — assembly identity token.

EASTL facts stay in the rbfx subtree; the generic registry and SharpCpp core do not reference them.
