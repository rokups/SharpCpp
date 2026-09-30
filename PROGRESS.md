# Open work

History lives in git. This file lists only what is open.

## Stage 0: freeze the contract

- [x] rbfx consumer split into its own repository (`SharpCpp.Rbfx`); its E2E suite
      builds and passes against the shared-library SDK.
- [x] `EntryPointPrefix` honoured by every renderer entry point.
- [x] Cross-module composition: per-module shared-wrapper helpers, imported aliases,
      marshaller headers and managed generic shapes carried in metadata.
- [x] Hygiene: personal tooling removed; `doc/` archived; absolute paths removed from
      the coverage script.
- [x] E2E fixtures grouped by C++ feature; separately generated modules live under `Modules/`.
- [x] Test-only wiring moved from `src/SharpCpp.Tool` into the
      `tests/SharpCpp.Tests.E2E.Generator` driver; it no longer counts toward coverage.
- [x] `SharpCpp.Tests.Model` / `SharpCpp.Tests.Metadata` deleted; their scenarios are
      E2E fixtures.
- [x] `AssignImportMethodNames` in one place; annotation caches reset per parse (the
      statics themselves go with the Stage 1 `ParseSession`).
- [x] `SharpCpp.Toolchain`: compiler-driven toolchain discovery used by the E2E driver
      and both SharpCpp.Rbfx generators.
- [x] RedBolt module generators (core, Glow, Network, PhysicsBox2D, PhysicsJolt,
      RmlUI, XR) use `SharpCpp.Toolchain` instead of private copies of the toolchain
      discovery.
- [x] Unit tests brought up to date with the fully-qualified managed-callback entry
      point, `char**` fields projected as `IntPtr`, and abstract constructors degraded in
      `DegradeUnsupportedSurfacePass`; the Model suite passes.
- [x] Coverage gate: 95.21% line coverage (`eng/collect_coverage.sh`, minimum 95%).

## Later stages

Typed model (`CppType`, typed facets, diagnostic codes, `clang::annotate` attributes),
marshaller catalog and crossing resolver, emitters, type library breadth, declarative
consumer configuration. Template specializations are already identified by their
complete canonical arguments; the rest of type identity is still string-based.

Known gaps (unbound or degraded today, with a diagnostic where the generator sees them):

- An abstract record without a director gets a hidden concrete wrapper
  (`__{Name}Native`) whose overrides call native code, so native pointers to unbound
  implementations wrap. The factory stays null (returning such a pointer throws) for generic
  records, records whose managed base is not rendered here, records that inherit abstract
  members from a director base, and pure virtuals that are operators, accessor properties,
  generic methods, or take pointer-array, undefaultable or native-default-dispatch parameters.
- Secondary-base pointer/reference parameters take the `I{Base}` view and polymorphic
  secondary-base pointer returns resolve to the most derived wrapper. What remains
  class-typed: by-value parameters, returns of non-polymorphic secondary bases, virtual
  methods, constructors, operators and accessor properties. Secondary bases that are generic,
  shared from another module, native interface wrappers, have no rendered interface members, or
  are reached by a non-plain record or through an ambiguous path also stay class-typed (logged
  as a warning on the base record). Objects constructed from managed code resolve through their
  director class's type id, registered as an alias of the record's wrapper (abstract director
  records have no registration, so their secondary-base returns still get a base wrapper). A
  secondary base that has its own secondary base with an interface fails to compile (CS0108:
  the interface redeclares the inherited base-interface methods; predates the view work).
- Open containers of a template parameter (`std::vector<T>` in a generic member) are not
  bound and are out of scope (no consumer uses them); such fields are reported instead of
  leaking an open type.
- RmlUi `Colour<...>`, `Vector3<T>` and `Vector4<T>` are parsed as non-generic records (ClangSharp
  reports `CXType_Unexposed`), so they are degraded and not emitted; `Vector2<T>` binds. This
  is a parser gap in template-record detection, separate from open containers.
- Every declaration the generator leaves out appears in `generator.log` (IGNORED, EXCLUDED or
  WARNING); the console stays clean. Warnings cover fields hidden by a same-named member
  (including an ignored one: the collision resolver skips ignored members, so the field is
  not renamed), counted-array accessors whose name is taken, bases lost when a generic base
  is dropped, removed generic instantiations, unmerged placeholder members and pruned type
  aliases (logged on the compilation). Clashes introduced by accessor properties generated after
  the pipeline are not diagnosed.
- Fields of class templates are bound per instantiation only as public, non-static value
  members (no references, pointers, arrays or bitfields). If the template has an explicit
  full specialization, all its fields are left unbound for every instantiation (reported,
  naming the specializations), and primary-template methods still render against the
  specialization.
- Capacity/count functions with a record output buffer keep the single record handle
  signature (needed for polymorphic buffers with a larger element stride, such as OpenXR
  `XrSwapchainImageBaseHeader`) and also get a `NativeArray<T>` overload that checks the
  capacity against the array length. The overload is not generated for virtual methods.
- A counted pointer-array field (matching `Num{X}`/`count{X}`/`{x}Count`) keeps its single
  pointer property and gets a read-only `Get{Field}At(nuint index)` that throws
  `ArgumentOutOfRangeException` past the count, in C and C++ modules: `T*`/`T**` of records,
  `T**` of opaque handles (`XrSpaceUserFB*`) returning the handle wrapper, and
  `const char* const*`/`char**` returning `string?` (UTF-8). Only record-value `T*` arrays get
  `NativeArray<T>` support; there are no setters. A `const char*` item typedef'd to another
  name is not recognized as a string array, and a pointer-to-pointer of other element kinds
  (for example primitives) still has no per-item accessor.
- Pointer-to-pointer parameters (`char**`, record `T**`) are ambiguous, so each gets a shape
  (`MetadataKeys.PointerPointerShape`, `array` or `single`), resolved by
  `PointerPointerShapeResolver` after the consumer `configureBeforeValidation` hook. Defaults:
  `T* const*`, and a non-const one with an adjacent value count that relates to it, are input
  arrays; every other one is a single output. A count relates when it is `argc` (with `argv` or
  a `char**` right after it), a specific `Num{X}`/`num_{x}`/`{x}Count`/`{x}_count`/`count{X}`
  whose stem equals the parameter's stem (case-insensitive, no underscores, Hungarian `pp`/`p`
  prefix and trailing `s` ignored: `NumPrerequisites`/`ppPrerequisites`; `word_count` or
  `NumVertices` before an unrelated out pointer do not), or a generic `count`/`num` not
  claimed by a pointer before it; narrower than `IsArrayCountParameter`; a pointer count such
  as `int* count` does not qualify. `[[sharpcpp::counted_by(n)]]` on the array parameter (or a
  consumer setting `MetadataKeys.CountedBy` to the count's native name) is the sanctioned way
  to name a count outside that heuristic, such as `n`: it works for `char**`, record `T**`
  and primitive/handle pointers, the count may be anywhere in the list, one count may serve
  several arrays, and the count must be an integer value parameter (an unknown name, a pointer
  count, or a non-pointer tagged parameter ignores the function with a reason). Precedence for
  `char**`/`T**`: an explicit `[[sharpcpp::in]]`/`[[sharpcpp::out]]` or a consumer-set shape
  different from the parser's default (`PointerPointerDefaultShape`) wins over `counted_by`; a
  consumer shape equal to the default is indistinguishable from it. `[[sharpcpp::in]]` /
  `[[sharpcpp::out]]` or a consumer overwriting the metadata value select the other shape; a
  value that cannot be honoured (unknown value, not a `char**`/record `T**`, an output through
  a const pointer) ignores the function with a reason in generator.log. Char arrays are
  `ReadOnlySpan<nint>` of NUL-terminated UTF-8 pointers (callers allocate the strings); an
  output `char**` is `out string?`, copied from the native pointer, which is borrowed and never
  freed (this changed the ownership of the previous explicit `[[sharpcpp::out]] const char**`,
  which the LibraryImport marshaller freed). Element kinds other than char and records
  (`int**`, `void**`) keep their previous binding.
- Open items for pointer-to-pointer shapes: (a) string arrays bind as `ReadOnlySpan<nint>`;
  the target is `ReadOnlySpan<string?>` with generator-side UTF-8 marshalling. (b) There is no
  owned out-string variant with a configurable free function (output `char**` is always
  borrowed). (c) The inheritance projection (`BindingCompilationInheritancePass`) does not copy
  `InputPointerArray`/`WritablePointerArray` (it does copy the pointer-pointer fields).
  The output-to-array flip uses a narrower count name set than the primitive-array heuristic
  (`argc`, `Num*`, `*Count*`; not `*Length`/`*Size`) and additionally requires the count name
  to relate to the parameter (see above), because a false positive silently changes
  an output into an array; an explicit `[[sharpcpp::in]]`/`[[sharpcpp::out]]` that cannot be
  honoured ignores the function with a reason.
- Containers whose element is not copy-assignable, or whose allocator (a record declaring
  `allocate`) is not default-constructible, are degraded with a diagnostic. The element check
  uses the same coarse heuristic as the non-copyable one (the container has a member taking or
  returning the element by value), so it can over-degrade. The allocator flag is set only on
  the AST template-argument path, not the CX fallback.
- Projected-value arrays (`Marshaller<T*>` / `Marshaller<const T*>` specialized with
  `ProjectedValueSpanMarshallerBase` / `ProjectedValueConstSpanMarshallerBase` and bound to
  `Span<T>` / `ReadOnlySpan<T>` through `SpanAbiMarshaller<>`) work for parameters of
  direct calls only. A return, field, virtual method or director callback parameter, or
  managed callback parameter or return of that pointer type has no marshaller for the
  position, so the declaration is degraded with the diagnostic "span projection '...' can
  only be used for a parameter of a direct call, not ..." (the UTF-8 `ReadOnlySpan<byte>`
  string views are unaffected). The minimum length comes only from
  `MetadataKeys.MinimumSpanLength` set by the consumer; there is no parser heuristic.
- Pointer ranges: `[[sharpcpp::ended_by(end)]]` on a begin pointer parameter (mirrors Clang
  `__ended_by`), or a consumer setting `MetadataKeys.EndedBy` to the end parameter's native
  name, binds `(T* begin, T* end)` as one managed span: `ReadOnlySpan<T>` for a const pointee,
  `Span<T>` for a mutable one. There is no name-based default and the tag is valid on
  parameters only. `end` is hidden from managed signatures but stays in `function.Parameters`
  at its native index, marked `DerivedArgument = PointerRangeEnd` /
  `DerivedFromParameter = begin` (invocation plans, `__Args{k}` variants and dispatch are
  index-aligned; only public signatures skip it). The wrapper passes `begin` = the pinned
  pointer and `end` = `begin + span.Length`; an empty span passes null/null. Validated by
  `PointerPointerShapeResolver.ApplyEndedBy`; every invalid tag ignores the function with a
  reason (unknown name or self, begin not a pointer, pointer-to-pointer begin, end of a
  different type, end not directly after begin, begin also `counted_by`, end itself
  `counted_by`/`ended_by`, no input array projection such as a record `T*`, template function
  or constructor). Fixtures: `Pointers/PointerRanges.hpp`. Open increments:
  (2) enum pointee, `T* const*` (no fixture yet) and record/char `T**` ranges
  (`ReadOnlySpan<T?>`; currently a `T**` begin is degraded with "pointer-to-pointer ranges are
  not supported yet", the plan reshapes begin to the `array` shape as `counted_by` does; a
  future `SpanLength` derived kind could hide a `counted_by` count the same way); (3) virtual
  methods, director callbacks and rbfx adoption; constructors are degraded today. Done: class
  template members and template functions are degraded with a reason (`CreateConcreteParameter`
  does not copy parameter metadata); the inheritance projection copies `EndedBy`,
  `DerivedArgument`, `DerivedFromParameter`, `InputPointerArray` and `WritablePointerArray`
  (fixture `range_both`); a native default on begin or end degrades the function.
