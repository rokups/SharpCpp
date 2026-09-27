# SharpCpp codemap

SharpCpp generates C# bindings for C++ headers: C# wrappers with `[LibraryImport]`
declarations, C++ thunks exported from a native library, and binding metadata that
lets other modules import the generated surface. How a type crosses the boundary is
configured by `Marshaller<T>` specializations in C++ headers.

## Build entry points

| Path | Role |
|---|---|
| `SharpCpp.slnx` | Aggregate build: managed projects, E2E generation, native build via `SharpCpp.Native` |
| `native/SharpCpp.Native/` | MSBuild project that drives the native CMake build |
| `CMakeLists.txt`, `CMakePresets.json` | Native build; exposes `SharpCpp::NativeSupport` for consumers |
| `eng/collect_coverage.sh` | Process-level E2E coverage gate (dotnet-coverage) |

## Projects

| Project | Role |
|---|---|
| `src/SharpCpp.Model` | Binding model (`BindingCompilation`, records, functions, `BindingType`), metadata keys |
| `src/SharpCpp.Parsing` | ClangSharp header parser, marshaller configuration extraction (`MarshallerConfigExtractor`, `MarshallerMetadataPhase2`) |
| `src/SharpCpp.Pipeline` | Ordered passes (`BindingCompilationBootstrapPipeline`): instantiation discovery, inheritance, validation, degradation |
| `src/SharpCpp.Rendering` | `BindingOutputRenderer`: C# and C++ emission, shared wrapper providers, generics |
| `src/SharpCpp.Metadata` | Metadata document, export and import for cross-module composition |
| `src/SharpCpp.Support` | Managed runtime: `NativeObject`, instance caches, provider registry, collection bases |
| `src/SharpCpp.Std` | Shared `Std.*` wrapper types (`Std.String`, `Std.Vector<T>`, ...) and their provider contracts |
| `src/SharpCpp.Toolchain` | Compiler-driven Clang toolchain discovery |
| `tests/SharpCpp.Tests.E2E.Generator` | E2E generator driver: parses the fixtures and applies test-only consumer wiring |
| `include/SharpCpp` | Native contract: `Marshaller.hpp`, `Export.hpp`, `detail/MarshallerSupport.hpp` (`MarshallerInvoker`) |

## Data flow

```text
headers + Marshaller<T> headers
  -> ClangSharpHeaderParser            (records, functions, types, attributes)
  -> BindingCompilationBootstrapPipeline (instantiations, inheritance, validate, degrade)
  -> BindingOutputRenderer             (*.g.cs, *.g.cpp)
  -> BindingMetadataExporter           (*.Bindings.json, imported by dependent modules)
```

## Tests

`tests/SharpCpp.Tests.E2E` is the specification: each fixture header is generated,
compiled into `SharpCppTestsE2E_Native`, and exercised from xunit. Fixtures are grouped
by C++ feature (`Aliases/`, `Callback/`, `Records/`, `StandardLibrary/`, ...); each
`*Marshallers.h` beside a fixture is loaded as marshaller configuration. Everything under
`Modules/` is generated as a separate binding module by the generator driver:
`CrossModule/` and `Metadata/` import another module's metadata, `NamespaceMapping/`
uses namespace mappings, `C/` parses a C header and `OwnedFiles/` uses owned file roots.
The exported `*.Bindings.json` documents are asserted through
`Diagnostics/GeneratedArtifacts.cs`. There are no unit test projects; E2E is the only suite.

## Consumers

RedBolt module generators and `SharpCpp.Rbfx` (a sibling repository) consume SharpCpp
only through its public projects and headers.
