# rbfx/

## Responsibility

The rbfx integration layer bridges SharpCpp's generic C++/C# binding generation pipeline to the rbfx game engine. It owns the complete end-to-end build flow for generating, compiling, and deploying rbfx-specific managed bindings, native glue libraries, and user-code extensions. This directory is the composition boundary where SharpCpp's library components (Model, Metadata, Parsing, Pipeline, Rendering, Support) are instantiated into a working binding generator for a concrete C++ SDK.

## Design

### Two-Phase Generation Architecture

The integration implements a **phase-1 / phase-2** generation model:

- **Phase 1 (SharpCpp.Rbfx.Generator)**: Parses the installed rbfx SDK headers via ClangSharp, applies the bootstrap pipeline, and emits C# bindings, C++ native glue, and a `Bindings.json` metadata document. The metadata document captures the shape of all SDK types and serves as the import contract for phase 2.

- **Phase 2 (Rbfx.UserCode.Generator)**: Parses user-authored C++ headers (e.g., `RotatingComponent.hpp`), imports the phase-1 metadata, and generates bindings for user types that reference SDK types. This allows user types to extend SDK classes while enjoying full managed projection.

### Build-Time Code Generation

All code generation occurs at CMake build time via `add_custom_command`. The generator DLLs are built first (via `sharpcpp_add_dotnet_build_target`), then invoked as `.NET` tools that write `*.g.cs` and `*.g.cpp` files into the build tree. Generated C++ sources are compiled into shared libraries; generated C# sources are linked into managed assemblies via wildcard `Compile` items in `.csproj` files.

### Staleness Management

A CMake script (`CheckGeneratedStaleness.cmake`) compares the generator DLL timestamp against a generation stamp file. If the generator has been rebuilt, the entire generated output directory is wiped before regeneration, preventing stale artifacts from surviving incremental builds.

### Runtime Dependency Deployment

A CMake template (`CopyRbfxRuntimeDependencies.cmake.in`) uses `file(GET_RUNTIME_DEPENDENCIES)` to discover and copy rbfx SDK shared libraries (`libUrho3D.so`, transitive deps) into the binary output directory alongside the generated native library. This ensures that managed test runners and the application shell can locate native symbols at load time.

### CMake Target Topology

```
sharpcpp-model,sharpcpp-metadata,sharpcpp-parsing,
sharpcpp-pipeline,sharpcpp-rendering,sharpcpp-support
      |
      v
sharpcpp-rbfx-generator  ---->  sharpcpp-generate-rbfx
      |
      v
SharpCppRbfx_Native (C++ shared lib, generated .g.cpp + SDK)
      |
      v
sharpcpp-rbfx-managed (C# assembly, generated .g.cs + Support)
      |
      +---> sharpcpp-rbfx-tests-e2e-managed
      +---> sharpcpp-rbfx-application (via Rbfx.Application)
      |
      v
sharpcpp-rbfx-usercode-generator  ---->  sharpcpp-generate-rbfx-usercode
      |
      v
SharpCppRbfxUserCode_Native (C++ shared lib, user .cpp + generated .g.cpp + SDK)
      |
      v
sharpcpp-rbfx-usercode-managed (C# assembly, generated .g.cs + Rbfx.Bindings + Support)
```

## Flow

### End-to-End Generation Pipeline

1. **SDK Discovery**: `RBFX_SDK_ROOT` points to an installed rbfx SDK. `find_package(Urho3D)` locates the SDK CMake config. Build flags, include directories, and compile definitions are recursively collected from the `Urho3D` CMake target into `RBFX_URHO3D_INCLUDE_DIRECTORIES`, `RBFX_URHO3D_COMPILE_DEFINITIONS`, `RBFX_URHO3D_COMPILE_OPTIONS`.

2. **Flags File Generation**: A per-config `urho3d-sdk-flags.txt` is written with `compiler_path`, `language_standard`, include directories, defines, and clang arguments. This file is consumed by both generators.

3. **Phase 1 Generation**: `SharpCpp.Rbfx.Generator.dll` is invoked with `--sdk-root`, `--flags-file`, `--generated-root`, `--project-root`, `--sdk-library`, `--log-file`. It:
   - Loads SDK parse flags
   - Discovers the host Clang toolchain (target triple, resource directory, system include paths)
   - Builds Clang arguments (standard, include dirs, defines, SIMD disable, warning suppress, toolchain flags)
   - Parses `Urho3D/Urho3DAll.h` via `ClangSharpHeaderParser`
   - Applies `BindingCompilationBootstrapPipeline` for marshaller rewrites
   - Filters functions not exported by the linked SDK library via `nm -D --demangle` symbol inspection
   - Renders C#/C++ output files via `BindingOutputRenderer`
   - Exports `SharpCpp.Rbfx.Bindings.json` metadata
   - Writes a `generate.stamp` to signal completion

4. **Phase 1 Native Build**: `SharpCppRbfx_Native` compiles the generated `.g.cpp` files with Unity (Jumbo) build, links against `sharpcpp-native-support` and `Urho3D`, and copies SDK runtime dependencies post-build.

5. **Phase 1 Managed Build**: `sharpcpp-rbfx-managed` compiles `SharpCpp.Rbfx.Bindings.csproj` which includes generated `.g.cs` files from the build tree.

6. **Phase 2 Generation**: `Rbfx.UserCode.Generator.dll` is invoked with `--sdk-root`, `--flags-file`, `--user-code-root`, `--generated-root`, `--import-binding-metadata` (pointing to phase 1 metadata). It:
   - Loads phase-1 binding metadata as imports
   - Parses `RbfxUserCode/RotatingComponent.hpp` (and future headers)
   - Applies the bootstrap pipeline with phase-1 types available as imported references
   - Renders output and exports its own metadata

7. **Phase 2 Native + Managed Build**: Same pattern as phase 1 but the managed assembly (`Rbfx.UserCode.Bindings`) references `SharpCpp.Rbfx.Bindings` and `SharpCpp.Support`.

8. **E2E Tests**: `sharpcpp.rbfx.e2e` ctest target runs `dotnet test` on `SharpCpp.Rbfx.Tests.E2E` with `LD_LIBRARY_PATH` and `LD_PRELOAD` configured for Linux.

9. **Application Shell**: `sharpcpp-rbfx-application` builds `Rbfx.Application.exe` which references both binding assemblies.

## Integration

| Consumer | What it depends on | Mechanism |
|---|---|---|
| `SharpCpp.Rbfx.Generator` | `SharpCpp.Model`, `SharpCpp.Metadata`, `SharpCpp.Parsing`, `SharpCpp.Pipeline`, `SharpCpp.Rendering` | .NET project references |
| `SharpCpp.Rbfx.Bindings` | `SharpCpp.Support`, generated `*.g.cs` | .NET project reference + wildcard Compile |
| `SharpCppRbfx_Native` | `sharpcpp-native-support`, `Urho3D` SDK target, generated `*.g.cpp` | CMake `target_link_libraries` + Unity build |
| `Rbfx.UserCode.Generator` | Same pipeline libraries as rbfx generator | .NET project references |
| `Rbfx.UserCode.Bindings` | `SharpCpp.Support`, `SharpCpp.Rbfx.Bindings`, generated `*.g.cs` | .NET project references + wildcard Compile |
| `SharpCppRbfxUserCode_Native` | `sharpcpp-native-support`, `Urho3D`, user `.cpp` files, generated `.g.cpp` | CMake `target_link_libraries` + Unity build |
| `Rbfx.Application` | `SharpCpp.Rbfx.Bindings`, `Rbfx.UserCode.Bindings` | .NET project references |
| `SharpCpp.Rbfx.Tests.E2E` | `SharpCpp.Rbfx.Bindings`, xUnit, .NET Test SDK | .NET project + package references |
| `rbfx/CMakeLists.txt` | `rbfx-sdk` installed at `RBFX_SDK_ROOT` | `find_package(Urho3D CONFIG)` |

### Directory Map

| Path | Role |
|---|---|
| `SharpCpp.Rbfx.Generator/` | Phase-1 SDK binding generator (ClangSharp-based, pipeline-enabled) |
| `SharpCpp.Rbfx.Bindings/` | Phase-1 managed assembly container (generated C# consumed via wildcard) |
| `SharpCpp.Rbfx.Tests.E2E/` | End-to-end xUnit tests exercising generated SDK wrappers |
| `Rbfx.UserCode/` | Phase-2 user-code generation + compilation (generator, bindings, native sources) |
| `Rbfx.Application/` | Application shell composing rbfx + user-code bindings |
| `CheckGeneratedStaleness.cmake` | Generator staleness checker (timestamp-based invalidation) |
| `CopyRbfxRuntimeDependencies.cmake.in` | Runtime library deployment script template |
