# SharpCpp Restart Progress

## Goal

Bootstrap an independent SharpCpp line from the design docs in `doc/`, while selectively copying small, bounded pieces from `../SharpCpp`.

## Current Status

- [x] Git repository initialized
- [x] Agent guide adapted
- [x] Independent repo/build skeleton started
- [x] Model foundation implemented
- [x] Metadata export/import foundation implemented
- [x] Parser skeleton implemented
- [x] Pipeline skeleton implemented
- [x] Rendering skeleton implemented
- [x] Unit test projects wired through CMake/ctest
- [x] First vertical slice: primitive/value parameters + simple handle returns + metadata export
- [x] Generated output contract wired through tool/CMake
- [x] E2E generator and runtime tests wired

## Current Focus

1. Broaden the ClangSharp-backed parser beyond the current builtin/enum/record-method/field/constructor/static-factory E2E fixture headers.
2. Expand the first slice beyond builtin primitives, enums, record methods, fields, constructors, and static factories into more native surface categories.
3. Keep feature scope narrow until the parse -> render -> runtime path is solid.

## Decisions So Far

- Use `SharpCpp.*` managed assembly identities.
- Use `sharpcpp-*` CMake targets.
- Keep structurally independent from `../SharpCpp`; reuse is copy/adapt only.
- Start with the early-grouped project split from `doc/11-project-shape-blueprint.md`: `Model`, `Metadata`, `Parsing`, `Pipeline`, `Rendering`, `Tool`, `Support`.
- The first E2E slice must go through ClangSharp rather than a permanently hardcoded binding model.
- Bootstrap the first exported artifact through metadata before attempting full parser/renderer breadth.
- Attribute-driven customization points should also be writable through the C# AST API so custom generators can mark nodes directly.

## Relevant Doc Anchors

- `doc/01-architecture.md`
- `doc/06-core-minimal.md`
- `doc/11-project-shape-blueprint.md`
- `doc/12-generated-output-and-module-api.md`
- `doc/13-rewrite-strategy.md`
- `doc/14-diagnostics-and-failure-model.md`

## Reuse Notes

- Candidate early copy: `CodeWriter` from `../SharpCpp/src/SharpCpp.Core/Utilities/CodeWriter.cs`
- Candidate early copy: diagnostic record/collector shape from `../SharpCpp/src/SharpCpp.Core/Diagnostics/`
- Avoid copying parser/rendering logic until the new contracts exist.
- Current generator path parses `tests/SharpCpp.Tests.E2E/Builtin/Builtin.hpp` through ClangSharp and emits the four generated artifacts under the build tree.

## Pending Work

1. Expand the ClangSharp parser from the narrow E2E header to more declarations and type kinds.
2. Introduce phase-2 marshaller metadata extraction contracts in `SharpCpp.Parsing`.
3. Grow the generated managed/native surface beyond the current primitive `int` + simple handle case.
4. Add more focused rendering and parsing tests as the feature surface grows.

## Current Bootstrap Slice

- `SharpCpp.Model` owns the initial mutable binding model, metadata bag, diagnostics, source references, and `CodeWriter`.
- `SharpCpp.Metadata` owns a deterministic metadata document, exporter, and serializer.
- `SharpCpp.Parsing` now contains a minimal ClangSharp-backed header parser for multiple E2E fixture headers, including explicit `[[sharpcpp::in|ref|out]]` parameter overrides and `[[sharpcpp::flags]]` enums.
- `SharpCpp.Rendering` owns the first generated `.Bindings.g.cs`, `.Bindings.Aliases.g.cs`, and `.Bindings.g.cpp` emitters.
- `SharpCpp.Tool` now drives both metadata-only output and the first generated binding pipeline.
- `tests/SharpCpp.Tests.E2E` now proves builtin primitive value/in/ref/out, explicit attribute-driven pointer pass kinds, enum/flags emission, and a simple handle return end-to-end.
- The same customization points are available through the binding model API today via `ParameterBinding.Direction`, `ParameterBinding.AbiKind`, `EnumBinding.IsFlags`, and `EnumBinding.Values`.
- Record instance methods now parse from C++ records, emit native self thunks, and surface as instance methods on managed handle wrappers.
- Public record fields now parse from C++ records, emit getter/setter thunks, and surface as managed properties on handle wrappers.
- Public record constructors now parse from C++ records, emit native allocation thunks, and surface as managed constructors on handle wrappers.
- Public static record methods now parse from C++ records, emit static thunks, and surface as `public static` members on managed wrappers.
- Handle-typed parameters on free/static methods now surface as wrapper objects in managed signatures and pass native handles through generated thunks.
- Handle-typed record fields now surface as wrapper-valued managed properties with getter/setter thunking.
- Handle-typed `ref` / `out` parameters now round-trip through wrapper-local ABI temps, so managed signatures stay wrapper-shaped even when native thunks mutate pointer values.
- Enums and flags now compose inside record constructors, static methods, and instance methods, not just as top-level free-function signatures.
- Read-only C++ fields now surface as getter-only managed properties.
- Simple single inheritance now maps to managed class inheritance, so derived wrappers reuse base methods and properties.
- Base-typed parameters and returns now accept derived wrappers and flow through the inherited wrapper hierarchy cleanly.
- Builtin and enum literal default parameters now surface directly in free functions, constructors, static methods, and instance methods.
- Minimal operator projection now works for member unary/binary operators and primitive conversion operators on records.
- Comparison operator pairs (`==`, `!=`, `<`, `>`) now project cleanly for record wrappers.
- Wrappers now track owned-vs-borrowed handle semantics and generated constructors opt into disposal through native delete thunks.
- Non-nullable wrapper parameters now treat runtime `null` as `nint.Zero`, so null-handle flows remain expressible without turning every handle surface into `T?`.
- Top-level free-function overloads now get distinct generated entrypoints and private import helpers, so overloads are safe in the first slice.
- Record static methods, instance methods, and constructors now get the same overload-safe naming treatment.
- Partial comparison/equality operator sets now fall back to named methods when C# requires a missing companion operator.
- Custom generators can now explicitly mark handle-returning functions as owned through the AST, without relying on naming heuristics.
- Record static and instance overloads now get distinct generated entrypoints and private import helpers too.
- C++ namespace-qualified types now correctly round-trip through the parser and renderer: `CppQualifiedName` on `RecordBinding` and `FunctionBinding` stores the full `ns1::ns2::Name` path, native C++ thunks use it for `reinterpret_cast`, `new`, `delete`, and `arg_t<>/ret_t<>` type arguments, while managed C# code uses the unqualified `NativeName`-derived PascalCase name. Entrypoint names remain flat (unqualified) since C linkage cannot encode `::`.
- `Marshaller<T>` now carries `ConvertToAbi(cpp_value)` and `ConvertToCpp(abi_value)` methods, with identity implementations for primitives/pointers/void* and address-of/dereference for references.
- Native thunks now use `MarshallerInvoker` invocation helpers (`invoke_as<FT>`, `invoke<FT>`, `construct`, `destroy`, `get_field`, `set_field`) instead of inline `reinterpret_cast`, `*param`, or pointer-pass-through. Generated code emits `using FT = Ret(Args...);` typedefs for clean template dispatch.
- `IsConstMethod` on `FunctionBinding` detected via `clang_CXXMethod_isConst` from ClangSharp, with `const` propagated to the `FT` member-function-pointer signature.
- `SharpCpp.Support` now contains `NativeObject : SafeHandle` — the base class for all generated wrapper classes. Ported from `../SharpCpp`, it provides GC-safe critical finalization, handle lifecycle management (`OwnsNativeHandle`, `ReleaseNativeHandle`, `DisposeFromNativeRuntimeCore`), `GCHandle`-backed managed keep-alive for shared ownership, virtual method override detection (`HasManagedOverride`), and static handle-extraction helpers (`GetObject`, `RequireObject`, `GetHandle`, `RequireHandle`).
- `INativeInstanceCache` interface was ported alongside `NativeObject` since the base class references it as the return type of its virtual `InstanceCache` property. The interface itself is minimal (3 methods); full instance cache implementations will land in a later slice.
- Wrapper class rendering now inherits from `global::SharpCpp.Support.NativeObject` instead of `global::System.IDisposable` directly. Root wrapper constructors chain to `base(handle, ownsHandle)` and surface `Handle`, `OwnsHandle`, and `IsNull` as forwarding properties to the base class. The previous `Dispose()` / `DisposeCore()` boilerplate is replaced by a `ReleaseNativeHandle` override that conditionally calls the native delete thunk only when `OwnsNativeHandle` is true.
- `[[sharpcpp::ignore]]` attribute support: the parser detects `[[sharpcpp::ignore]]` (with optional reason string like `[[sharpcpp::ignore("reason")]]`) on records, enums, free functions, member methods, constructors, and fields using a combination of cursor-extent source text and a pre-scanned annotation index. When detected, `MetadataKeys.IgnoreReason` is set on the binding node. The renderer emits commented-out pseudo-code stubs (e.g. `// class Name; // excluded: reason`) instead of full bindings for any ignored element. Ignored elements are also excluded from C++ native thunk generation.
- All `[LibraryImport(...)]` attribute lines are now immediately followed by the method signature with no intervening blank line, regardless of whether a `[return: MarshalAs(...)]` attribute is present.
- C++ documentation comments (`///` line comments and `/** */` block comments) are now carried over to the C# public API. The parser extracts `cursor.BriefCommentText` from libclang at each binding node creation site and stores it via `MetadataKeys.DocumentationComment`. The renderer emits `/// <summary>{escaped_text}</summary>` XML doc comments before managed records, enums, free functions, instance/static methods, constructors, operators, and field properties. XML special characters (`<`, `>`, `&`, `"`) are automatically escaped.
- Removed `AbiTypeAliasExpression` regex, `AbiTypeName` property from `MarshallerDescriptor`/`BindingType`, and `AbiType` field from metadata `BindingMetadataTypeReference`. Also dropped dead renderer methods `GetNativeAbiTypeName`, `BuildNativeFunctionSignatureWithSelf`, and dead `HasCustomMarshaller` property. Marshaller config loading (`ManagedTypeMarshallerExpression`, `PublicTypeExpression`, `LoadManagedTypeOverrides`) retained as scaffolding.
- All `[LibraryImport]` P/Invoke declarations are now grouped into a nested `internal static partial class __PInvoke` inside each wrapper class and the free-function static class, instead of being interleaved as `private static partial` members directly on the wrapper. The `__PInvoke_` prefix is dropped from method names; call sites use `__PInvoke.MethodName(...)`. Derived wrappers use `new` to override the base `__PInvoke`.
- Overload disambiguation for P/Invoke method names now uses a simple counter instead of parameter-type suffixes: the first overload keeps the base `ManagedName`, the second gets `ManagedName2`, etc. Counter assignment runs as a post-pass in both the parser and the renderer (`AssignImportMethodNames`), with per-group scoping (per-record for members, per-compilation for free functions). Parameter-type-based `SanitizeManagedImportNamePart` was removed.

## Verification Commands

```bash
cmake --preset dev
cmake --build --preset Debug
ctest --preset Debug
```
