# Undefaultable Types — Implementation Plan

## Problem

Some C++ types fundamentally cannot have default values in C#:
- `const char*` marshalled as `ReadOnlySpan<byte>` — no sensible null/default
- Other pointer/opaque types where `null` isn't a valid marshalling convention

Currently, parameters with unexpressible defaults become `T? = null` with an if/else cascade. For types that shouldn't have defaults at all, we need a different approach: **overloads**.

## Design

### Marshaller attribute extension

Add `undefaultable = true` to the `[[sharpcpp::managed_type(...)]]` attribute.
Default is `false` (backward compatible).

```cpp
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    undefaultable = true
)]] Marshaller<const char*> { };
```

### Overload generation rule

When a function has parameters of `undefaultable` types with C++ defaults:

```
C++: void foo(const std::string& a = "a", const char* b = "", const std::string& c = "b");

C#:
  // Overload A: params before first undefaultable, with normal defaults
  void foo(Std.String? a = null);                          // covers foo() and foo("a")
  // Overload B: all params, undefaultable are required, before-params lose defaults
  void foo(Std.String a, ReadOnlySpan<sbyte> b, Std.String? c);  // covers foo("a","b") and foo("a","b","c")
```

**Algorithm:**
1. Find the first parameter with `MetadataKeys.UndefaultableParam` set
2. **Overload A**: keep params [0, firstUndefaultableIndex) with their normal defaults.
   - If before-params have `UnexpressibleDefault`, Overload A gets its own cascade sub-dispatch.
   - Native entrypoint for Overload A: `{orig}__Args{firstUndefaultable}` (or further cascading variants).
3. **Overload B**: all params. Rules:
   - `index < firstUndefaultable`: **required, no default** (must be supplied because C# can't skip them before a required param)
   - `index >= firstUndefaultable AND undefaultable`: **required, no default**
   - `index >= firstUndefaultable AND NOT undefaultable`: **normal default rules** (literal default or nullable `= null`)
4. Both overloads share native entrypoints through the existing `__ArgsN` system. For positions where `firstUndefaultable == 0` and no `UnexpressibleDefault` params exist, a `__Args0` native thunk must be generated (this variant already follows the same pattern as existing `EmitUnexpressibleDefaultVariant`).

### `out`/`ref` params

`out` params already strip defaults (doc/16 line 193). `ref` params can't have defaults in C# (doc/16 line 222). Undefaultable + `out`/`ref` is a no-op — covered by existing rules.

## Implementation Phases

### Phase 1: Model/metadata keys

**Files:**
- `src/SharpCpp.Model/Ast/MetadataKeys.cs`
- `src/SharpCpp.Model/Ast/BindingType.cs` (MarshallerDescriptor)

**Changes:**
1. Add `MetadataKeys.UndefaultableParam = "undefaultable_param"` to `MetadataKeys.cs`
2. Add `Undefaultable` to `MarshallerDescriptor` as **non-positional** `{ get; init; } = false`:
   ```csharp
   public sealed record MarshallerDescriptor(
       string CppTypeName,
       string PublicTypeName,
       string? MarshallerTypeName = null)
   {
       public bool Undefaultable { get; init; }
   }
   ```
   This is backward-compatible — all creation sites continue working unchanged.
3. Add `BindingType.HasUndefaultableMarshaller` convenience property (reads `Marshaller.Undefaultable`).

### Phase 2: Parsing — read `undefaultable` from marshaller attributes

**Files:**
- `src/SharpCpp.Parsing/MarshallerMetadataPhase2.cs`
- `src/SharpCpp.Parsing/ClangSharpHeaderParser.cs`

**Changes in MarshallerMetadataPhase2:**
1. Add regex for `undefaultable\s*=\s*(?<value>true|false)`
2. When parsing `managed_type(...)` body and finding `undefaultable = true`:
   - For `Marshaller<T>` that targets a record (handle type): set `RecordBinding.SetMetadata("undefaultable", true)` — this will be read during descriptor construction
   - The key insight: MarshallerMetadataPhase2 transforms record metadata. The actual `MarshallerDescriptor` is constructed during header parsing (Phase 1 in `ClangSharpHeaderParser`), and `MarshallerMetadataPhase2` runs *after* Phase 1 as part of the pipeline. So the flag must flow from the `RecordBinding` metadata into `BindingType.MarshallerDescriptor` via a fixup pass.

Wait — this is a sequencing issue. `MarshallerMetadataPhase2` runs as a pipeline pass AFTER parsing. But `BindingType` instances are created during parsing with `MarshallerDescriptor` already constructed. However, looking at the code more carefully:

The `MarshallerDescriptor` carries `CppTypeName` and `PublicTypeName`. For handle types, `PublicTypeName` is set at construction time from the `record.ManagedTypeFormat` (which itself gets updated by `MarshallerMetadataPhase2` via `ApplyManagedTypeFormats`). So there's already a fixup pattern: `ApplyManagedTypeFormats` updates `BindingType` instances after metadata parsing.

The same pattern works for `Undefaultable`: after `MarshallerMetadataPhase2` runs, add a post-processing step that walks all `BindingType` instances and sets `MarshallerDescriptor.Undefaultable` for types whose referenced record has the metadata flag.

But `MarshallerDescriptor` is a `record` — immutable. So we need a different approach. Options:
a) Store `undefaultable` as metadata on `ParameterBinding` directly (no descriptor change needed)
b) Make `MarshallerDescriptor` mutable or add a setter
c) Use a separate dictionary on `BindingCompilation` to track undefaultable types

**Simplest approach**: Skip the `MarshallerDescriptor.Undefaultable` property entirely. Instead, during parsing, when `ApplyManagedDefaultValue` detects an undefaultable type:
- For handle types: look up the referenced `RecordBinding` and check its metadata (set by phase 2)
- For configured types (`const char*` etc): check the `ManagedTypeOverride` dictionary

BUT wait — `ApplyManagedDefaultValue` runs during parsing, BEFORE `MarshallerMetadataPhase2`. So it can't read record metadata set by phase 2.

**Revised approach**: Since the main case for undefaultable is non-record types (like `const char*`), and these are handled through `ManagedTypeOverride` in `LoadManagedTypeOverrides` (which runs at parse time), we can:
1. Parse `undefaultable` in `LoadManagedTypeOverrides` and store in `ManagedTypeOverride`
2. In `ApplyManagedDefaultValue`, check via `ManagedTypeOverride` for configured types
3. For record types, defer to a `MarshallerMetadataPhase2` fixup that sets param metadata

But actually, `const char*` isn't a record type at all. It goes through the pointer/override path. So the simplest thing: handle the common case first (pointer types with `undefaultable = true`), and leave record-type undefaultable for later if needed.

Actually, let me look at this differently. `MarshallerMetadataPhase2` also reads `managed_type()` from marshaller headers. The same regex that parses `public_type` could also parse `undefaultable`. And since marshaller headers are read at parse time (not through the pipeline), the info is available during parsing.

Wait, `MarshallerMetadataPhase2` IS the mechanism that reads marshaller headers in the pipeline. But `ClangSharpHeaderParser.LoadManagedTypeOverrides` ALSO reads marshaller headers at parse time, for the specific purpose of configuring `const char*` etc.

So there are TWO places that read `managed_type()`:
1. `ClangSharpHeaderParser.LoadManagedTypeOverrides` — reads `ManagedTypeMarshallerExpression` and `ReturnMarshallerExpression` (simpler regex, just gets public_type). Runs at PARSE time.
2. `MarshallerMetadataPhase2` — reads `ManagedTypePartialMarshallerExpression` and `ManagedTypeExactMarshallerExpression` (richer regex, gets full body including members). Runs in PIPELINE.

For the `undefaultable` flag, we need it at parse time for `ApplyManagedDefaultValue` to use. The simplest approach:

**For `const char*` (non-record override path):**
- Extend `LoadManagedTypeOverrides` regex or parsing to also read `undefaultable`
- Store in `ManagedTypeOverride` (extended record)
- In `ApplyManagedDefaultValue`, check `managedTypeOverrides` for the param's C++ type

**For record types (handle path):**
- `CreateHandle` creates `MarshallerDescriptor` during parsing
- If the record's `Marshaller<T>` has `undefaultable = true`, the info needs to be available during `ApplyManagedDefaultValue`
- But the record is registered in `RegisterRecord` with its name BEFORE marshaller metadata is applied
- Solution: pass the loaded `managedTypeOverrides` info through to record registration too

Actually, the cleanest path is to NOT try to store `Undefaultable` in `MarshallerDescriptor` at all. Instead:
1. Parse `undefaultable` in `LoadManagedTypeOverrides` (for non-record types like `const char*`)
2. Also in `MarshallerMetadataPhase2`, when processing record marshallers with `undefaultable`, store it on the `RecordBinding` metadata
3. Then add a new late pass (or extend an existing one) that walks all functions and sets `MetadataKeys.UndefaultableParam` on appropriate parameters
4. Or, even simpler: do a two-pass approach where `ApplyManagedDefaultValue` is called during parsing, and `undefaultable` for handle types is resolved in a pipeline pass after `MarshallerMetadataPhase2`

Actually, I realize there's an even simpler option. The `IsTypeUndefaultable` check doesn't need to be at parse time. It can be a pipeline pass that runs AFTER `MarshallerMetadataPhase2`. The pass walks all functions and sets `MetadataKeys.UndefaultableParam` based on:
- For each param with a default value:
  - If the type references a record with `undefaultable` metadata → set flag
  - If the type's C++ name matches a `ManagedTypeOverride` with `undefaultable` → set flag

This keeps parsing simple and moves the complexity to a focused pipeline pass.

Let me revise the plan accordingly.

### Phase 2 (revised): Pipeline pass for undefaultable param marking

**Instead of marking at parse time, mark in a pipeline pass.**

Add `MarkUndefaultableParamsPass` (or merge into an existing pass) that:
1. After `MarshallerMetadataPhase2` has set record metadata
2. Walk all functions (compilation + records)
3. For each param that has `ManagedDefaultValueExpression != null` or `HasDefaultArg` (check via CppTypeName):
   - Look up if the C++ type is known as undefaultable (via record metadata or stored overrides)
   - If yes, set `MetadataKeys.UndefaultableParam`

But wait, we need the `ManagedTypeOverride` info to be available in the pipeline. Currently `LoadManagedTypeOverrides` returns a dictionary used within `ClangSharpHeaderParser.Parse`. We'd need to store it somewhere accessible to the pipeline.

Hmm, this is getting complex. Let me take a step back.

**Simplest viable approach**: 
1. Parse `undefaultable` in `LoadManagedTypeOverrides` (for non-record types like `const char*`)
2. In `ApplyManagedDefaultValue`, check `managedTypeOverrides` for non-record types
3. For record types, support will be deferred or handled in a pipeline pass

Actually, there's one more option: don't use metadata at all for the first slice. Just hardcode the check for what we know is undefaultable: when a `const char*` has a default, and it's mapped to `ReadOnlySpan<byte>`, it's undefaultable.

But that's too fragile. Let me go with the pragmatic approach:

**Final Phase 2 design:**
1. Add `undefaultable` to `ManagedTypeOverride` record (non-positional property)
2. Parse `undefaultable` from `managed_type()` body in `LoadManagedTypeOverrides`
3. In `ApplyManagedDefaultValue`, for params whose C++ type matches a `ManagedTypeOverride` with `Undefaultable = true`, set `MetadataKeys.UndefaultableParam` on the parameter. This happens at parse time.
4. For record types (handle types), defer: add a fixup step in `ApplyManagedTypeFormats` (runs at pipeline time, already has the pattern of updating `BindingType`) that also propagates `undefaultable` from record metadata to `BindingType` — and then add a secondary `MarkUndefaultableParamsPass` or extend the existing param metadata pass.

Actually, the simplest approach for handle types: `ApplyManagedTypeFormats` already updates `BindingType` instances (via `type.SetManagedTypeFormatOverride`). We could add a similar mechanism where after phase 2 runs, we update `BindingType.MarshallerDescriptor` with `Undefaultable`. But `MarshallerDescriptor` is a record...

OK let me just be pragmatic. For the initial implementation:
1. Handle non-record types through `ManagedTypeOverride` at parse time (the main use case: `const char*`)
2. Record types: add a post-phase-2 pass that walks all function params and checks the referenced `RecordBinding` metadata for `undefaultable`, then sets `MetadataKeys.UndefaultableParam` on the parameter
3. Use a shared dictionary or compilation-level metadata to make the `ManagedTypeOverride` info available in the pipeline

This means Phase 2 has two parts: 2a (parse-time marking for non-record types) and 2b (pipeline marking for record types).

OR... I can just add a new field to `BindingCompilation` to carry the undefaultable C++ type names from parse time into the pipeline.

Actually, the simplest of all: drop `MarshallerDescriptor.Undefaultable` entirely. Don't touch `BindingType`. Instead:
- Parse `undefaultable` into `ManagedTypeOverride` at parse time (for pointer/override types)
- Set `MetadataKeys.UndefaultableParam` on params during `ApplyManagedDefaultValue` (for non-record types, at parse time)
- For record types: add a pipeline pass that reads record metadata (set by phase 2) and sets param metadata
- The renderer checks `MetadataKeys.UndefaultableParam` — it doesn't care where it was set

This minimizes model changes and keeps the implementation focused.

---

OK, I'm overthinking this. Let me finalize the approach:

## Final Plan (Simplified)

### Phase 1: Model changes

1. Add `MetadataKeys.UndefaultableParam = "undefaultable_param"` to `MetadataKeys.cs`
2. Add `bool Undefaultable { get; init; }` to `ManagedTypeOverride` record (non-positional, default false)
3. Add `BindingType.UndefinedDefaultableMarshaller` convenience property (reads `Marshaller.Undefaultable`)

### Phase 2: Parse undefaultable and mark params

**2a: Parse-time marking (non-record types like `const char*`)**
- Parse `undefaultable` from `managed_type()` body in `LoadManagedTypeOverrides`
- Store in `ManagedTypeOverride.Undefaultable`
- In `ApplyManagedDefaultValue`, check `managedTypeOverrides` for the param's C++ type
- If found with `Undefaultable = true`, set `MetadataKeys.UndefaultableParam` on `ParameterBinding`

**2b: Pipeline marking (record types)**
No — defer record-type undefaultable support. The initial use case (`const char*`) is non-record. Record types can be added later when needed.

### Phase 3: Rendering — emit overloads

In `BindingOutputRenderer.cs`:
1. Add `HasUndefaultableParams(FunctionBinding)` check (looks for `MetadataKeys.UndefaultableParam`)
2. Add `GetFirstUndefaultableParamIndex(FunctionBinding)` helper
3. When `HasUndefaultableParams` is true, call `WriteUndefaultableOverloads` instead of normal rendering path
4. `WriteUndefaultableOverloads`:
   - Finds `firstUndefaultable` index
   - **Overload A**: renders params [0..firstUndefaultable) with normal defaults. If these have `UnexpressibleDefault`, cascades for them. Native entrypoint: `{orig}__Args{firstUndefaultable}`.
   - **Overload B**: renders all params. Before/first undefaultable: required, non-nullable. Later undefaultable: required, non-nullable. After non-undefaultable: normal defaults.
5. Import declarations: Overload A and B each need imports. Overload A uses `__Args{firstUndefaultable}` and any lower variants needed for its own cascade. Overload B uses the full entrypoint and any after-undefaultable cascade variants.
6. Native thunks: Extend the variant generation to include indices from undefaultable params (for `__Args0` when `firstUndefaultable == 0`).

### Phase 4: Marshaller.hpp — add undefaultable to const char*

```cpp
template <>
struct [[sharpcpp::managed_type(
    public_type = "global::System.ReadOnlySpan<byte>",
    undefaultable = true
)]] Marshaller<const char*>
{
    // ...
};
```

### Phase 5: Tests

New E2E fixture covering:
- Single undefaultable param
- Mixed expressible + undefaultable params
- All-undefaultable params (firstUndefaultable == 0)
- Undefaultable + unexpressible defaults mixed
- Free functions, constructors, static methods, instance methods
- Reflection verification of overload parameter types and defaults

Plus rendering unit tests for the emitted overload shape.

### Phase 6: Verification

1. Run rendering tests
2. cmake --build --preset Debug
3. ctest --preset Debug
4. Update PROGRESS.md
5. Git commit

## Files Changed

| File | Change |
|---|---|
| `src/SharpCpp.Model/Ast/MetadataKeys.cs` | Add `UndefaultableParam` key |
| `src/SharpCpp.Parsing/ClangSharpHeaderParser.cs` | Parse undefaultable in overrides, mark params, update `ManagedTypeOverride` |
| `src/SharpCpp.Rendering/BindingOutputRenderer.cs` | Add overload emission logic |
| `include/SharpCpp/Marshaller.hpp` | Add `undefaultable = true` to `Marshaller<const char*>` |
| `tests/SharpCpp.Tests.E2E/Undefaultable/Undefaultable.hpp` | New test fixture header |
| `tests/SharpCpp.Tests.E2E/Undefaultable/Undefaultable.cs` | New test file |
| `PROGRESS.md` | Update |

## Risk Assessment

| Risk | Mitigation |
|---|---|
| Overloads collide with existing C++ overloads | Generate unique managed names; existing `AssignImportMethodNames` handles this |
| Overloads confuse DegradeUnsupportedSurfacePass | Degradation runs BEFORE rendering; pass sees only canonical functions |
| Nullable vs non-nullable confusion in overload B | Clear `UnexpressibleDefault` from params at/before firstUndefaultable when rendering overload B |
| Multiple undefaultable params | ALL undefaultable params in Overload B are required; position only determines where Overload A cuts off |
| `__Args0` missing when firstUndefaultable = 0 | Variant-generation indices include undefaultable param positions |
| Constructor factory methods | Each overload needs its own factory method; follow same overload pattern as instance methods |
