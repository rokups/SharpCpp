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
  records have no registration, so their secondary-base returns still get a base wrapper).
  Nested secondary-base interfaces (`ILabelled : ITickable`) inherit base-interface members
  instead of redeclaring them, and explicit accessor forwarders name the declaring interface
  (fixture `secondary_bases::labelled`). A covariant override of a secondary-base
  virtual (`tickable* self()` / `labelled* self() override`) is degraded with a reason
  (`DegradeCovariantSecondaryBaseOverridesPass`) and the base declaration is bound in its place,
  since the managed interface exposes the base return type; the projected base method is bound
  non-virtually (with a warning) so a director cannot be affected, and its native member pointer
  names the declaring record (fixtures `labelled`, `lighthouse`, `signal_tower`). Secondary-base
  method projection prefers the most-derived declaration: `EnumerateProjectableMethods` yields the
  secondary base itself first, then its bases nearest-first, and skips clones an earlier iteration
  projected into a base record (`MetadataKeys.ProjectedSecondaryBaseMethod`). A covariant override
  on the secondary base's own primary chain is therefore projected with its covariant return type
  and dispatch cast (fixture `origin_user`), and a non-virtual hiding declaration carries the
  hiding declaration's docs, defaults and return type (fixture `scale_holder`). Known gaps:
  non-virtual hiding with a different return type across a secondary base still only warns
  (`SecondaryBaseViewPass.WarnOnRedeclaredInterfaceMethods`) and gives CS0738; so does a covariant
  override on a secondary base's primary chain when the overridden base is itself a rendered
  interface (the class member cannot implicitly implement the inherited base-typed member, so no
  fixture exists); projection through a virtual-base diamond does not apply the C++ dominance
  rule (the shared virtual base is enumerated before the second path's hiding declaration); and
  a hiding declaration with a different parameter list still gets the base overloads projected,
  although C++ lookup through the hiding record cannot find them (last two found by reading, no
  fixture). A promoted accessor
  owned by a non-view class that reaches an interface only through a secondary base lacked an
  explicit implementation (CS0535 when the class redeclared `get_x`/`set_x`, fixtures `gauge` and
  `meter`); `WriteSecondaryBaseViewAccessorForwarders` now also runs for non-view classes, and a
  promoted base accessor no longer makes a projected plain method of the same name emit a
  spurious `new` (CS0109, fixture `rated_ticker`). `rated_ticker` keeps `tickable`'s
  `GetRate`/`SetRate` as plain methods beside the inherited `Rate` property (the two C++ members
  are distinct; the unqualified C++ call is ambiguous). A setter-only redeclaration (`splitter`)
  was fine. Redeclaring only one accessor of a pair across a secondary base (`meter`) pairs the
  hiding getter with the base setter through the interface.
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
  the pipeline are not diagnosed, except reserved names (see below).
- Reserved member names: every wrapper derives from `NativeObject : SafeHandle`, so a bound
  function whose managed name is in `DegradeUnsupportedSurfacePass.ReservedInstanceMemberNames`
  (`Close`, `Dispose`, `DangerousGetHandle`, `DangerousAddRef`, `DangerousRelease`,
  `SetHandleAsInvalid`, `SetHandle`, `ReleaseHandle`, `IsClosed`, `IsInvalid` plus the instance
  members of `NativeObject`: `OwnsNativeHandle`, `ManagesInstanceCacheLifecycle`, `InstanceCache`,
  `ReleaseNativeHandle`, `PrepareForOwnedRegistration`, `TryPromoteToOwningHandle`,
  `AcquireOwningHandleCore`, `DisposeFromNativeRuntimeCore`) is bound as `{Name}{ClassName}`
  (`close` on `base_window` becomes `CloseBaseWindow`) at any arity, with a WARNING in
  `generator.log`. `ClassName` is the simple managed name of the owning record (no namespace, enclosing
  type or generic argument list; a record projected onto a type the generator does not emit, by
  `suppress_emission` or a wrapper assembly, uses its native name in PascalCase, so `JPH::BodyID::IsInvalid`
  becomes `IsInvalidBodyID`, not `IsInvaliduint`); for a virtual function it is the record that first declares the
  virtual, found by following `OverriddenNativeDeclarationIds` transitively (the first resolvable
  overridden declaration at each step), so `base_window::close` and the override
  `deep_window::close` are both `CloseBaseWindow`. Overloads of one name in a record
  share one replacement (`CloseOverloadedWindow()` and `CloseOverloadedWindow(int)`, never
  `CloseOverloadedWindow2`); if a member outside the overload set already uses the name, a non-virtual set
  gets a counter suffix (`CloseCounterWindow2`). The
  four static `NativeObject` helpers (`GetObject(object?)`, `GetHandle(object?)`,
  `TransferHandleOwnership(object?)` and `HasManagedOverride(Type, Type, string, Type[])`) are
  renamed only when the function has the helper's exact signature (same parameter count and the same
  by-value managed parameter types); any other signature, such as `GetObject(uint)`, is a plain
  overload and keeps its name. An overload set containing a virtual function whose `{Name}{ClassName}` is
  already taken by a member outside the set is ignored with a reason instead of getting a counter
  suffix; so is a set whose virtual functions override virtuals first declared in different records. `new` is not used, so `x.Close()` always disposes. Every override in a chain shares the root's
  replacement, so overrides stay consistent. `ResolveReservedMemberNames` runs before the field collision resolver. To pick
  its own name a consumer uses the header `[[sharpcpp::rename("Name")]]` or, when setting
  `FunctionBinding.ManagedName` in code before the pipeline, also sets
  `MetadataKeys.ExplicitManagedRename` to `true`; such functions are left alone (the author must
  avoid a CS0108 clash, for example with a different signature). `GenerateAccessorPropertiesPass`
  refuses to create a property with a reserved name and logs a WARNING (appended to the log
  through `BindingDiagnosticLog.AddLateWarning`, since the log is written before consumer passes).
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
  a const pointer) ignores the function with a reason in generator.log. Input char arrays
  and ranges (`PointerPointerKind.Chars` with `InputPointerArray`) are
  `ReadOnlySpan<string?>`: the wrapper allocates NUL-terminated UTF-8 copies
  (`Marshal.StringToCoTaskMemUTF8`, null element = null pointer), passes them as the same
  pointer-to-pointer as before and frees them in a `finally`. A mutable `char**` (a non-const
  pointee, formerly `Span<nint>`) is also input-only: native may rewrite its elements, so the
  wrapper frees its own separately kept pointers and native writes are not read back. Native
  code must not retain the array or its strings after the call returns (they are freed in the
  `finally`). The `pointers` shape keeps the previous raw `ReadOnlySpan<nint>` binding for a
  function that retains them: `[[sharpcpp::shape(pointers)]]` on the parameter (also on an
  `ended_by` range begin; `shape(array|single|pointers)` is the general form, validated like a
  consumer value, and a conflict with `[[sharpcpp::in]]`/`out`/`ref`, an unknown value or a
  non-`char**`/`T**` parameter ignores the function with a reason) or a consumer-set
  `MetadataKeys.PointerPointerShape` value `pointers` (`PointerPointerShapes.Pointers`, `char**`
  only). It sets `RawStringPointerArray`, which makes the renderer take the generic raw array
  path: the caller owns the array and the UTF-8 strings (nothing is allocated or freed), and a
  mutable `char**` binds as `ReadOnlySpan<nint>` with no writability handling. Fixtures:
  `Pointers/RawStringPointers.hpp`. A director callback with a `pointers` range begin has no fixture
  yet: by inspection it receives an in-place `ReadOnlySpan<nint>` over the native pointers,
  valid only during the callback, as a `void* const*` range does. `pointers` combined with an
  explicit `counted_by` has no fixture either. A NULL-terminated string list (HarfBuzz
  `shaper_list`) is marked with `[[sharpcpp::null_terminated]]` on the parameter or a consumer-set
  `MetadataKeys.NullTerminatedStringArray` flag: the wrapper allocates one extra zero element
  after the strings (a count parameter is passed unchanged, the terminator is not counted). An
  empty span still passes a null pointer, so an empty non-null list is not expressible. The flag
  is valid only on a decoded input string array (not an `ended_by` range, a `pointers` array or
  a single output; it never changes the shape, so a count-less mutable `char**` needs
  `[[sharpcpp::in]]` too) or the function is ignored with a reason. There is no heuristic
  detection, and a director callback receiving a NULL-terminated list without a count stays
  unsupported (ignored: no count parameter). Fixtures: `Pointers/NullTerminatedStrings.hpp`. An
  output `char**` is `out string?`, copied from the native pointer, which is borrowed and never
  freed (this changed the ownership of the previous explicit `[[sharpcpp::out]] const char**`,
  which the LibraryImport marshaller freed). Element kinds other than char and records
  (`int**`, `void**`) keep their previous binding.
- Open items for pointer-to-pointer shapes: (a) string arrays are done for direct calls and
  non-director virtual methods, and a director callback with a char range or a counted
  `char**` receives managed string copies (`ReadOnlySpan<string?>`; the count stays a visible
  parameter of the override). The resolver records the count's native name as
  `StringArrayCount` (the `counted_by` count, else the adjacent count parameter the default
  shape pairs with, before first); a decoded `char**` director parameter without one ignores
  the director with a reason that asks for `counted_by`, and a raw `pointers` counted array
  in a director stays unsupported (ignored: the parameter is not an in-value). Fixtures:
  `Pointers/StringArrayVirtuals.hpp`. Still open: string array fields (`Get{Field}At`), output
  `char**` as `out string?` arrays. (b) The output-to-array flip uses a narrower count name set than the primitive-array heuristic
  (`argc`, `Num*`, `*Count*`; not `*Length`/`*Size`) and additionally requires the count name
  to relate to the parameter (see above), because a false positive silently changes
  an output into an array; an explicit `[[sharpcpp::in]]`/`[[sharpcpp::out]]` that cannot be
  honoured ignores the function with a reason.
- Owned (caller-freed) strings: `[[sharpcpp::freed_by(release)]]` names the free function of a
  native string, on an output `char**` parameter (anywhere in its extent, like `counted_by`) or
  before the decl-specifiers of a function returning `char*`/`const char*`
  (`[[sharpcpp::freed_by(release_text)]] inline char* make_text();`). A consumer sets
  `MetadataKeys.FreedBy` to the free function's native name on the `ParameterBinding` or
  `FunctionBinding` instead; C headers parsed as `-std=c17` cannot carry `[[...]]`, so they must
  use the consumer path. Managed shapes: the return is `string?` (never a span) and the output
  is `out string?`; the wrapper copies the UTF-8 text with `Marshal.PtrToStringUTF8` and calls
  the free function in a `finally`, skipped when the pointer is null (an unwritten or null
  result is never freed). The free runs through a generated native thunk
  `{EntryPointPrefix}_free_{name}` (`{cpp free function}(static_cast<{param type}>(value))`),
  emitted once per distinct free function per compilation: in the single `.g.cpp` or the first
  group's, with its `[LibraryImport]` `__PInvoke.__free_{name}` in the shared
  `.Bindings.Aliases.g.cs`, so the free runs in the allocator's own module. `OwnedStringResolver`
  (after the pointer-pointer shapes) validates the tag, records `MetadataKeys.StringFreeFunction`
  (the only key the renderer reads) and retypes an owned return to a raw address. It ignores the
  function with a reason in generator.log when: the attribute is empty; the free function is not
  exactly one non-generic, non-member function of the compilation and, when the compilation has
  none, not exactly one imported release thunk of that name (missing, overloaded, or exported by
  several imported modules); the
  free function does not take one pointer parameter (`void*` counts); a tagged parameter is not an
  output string (`[[sharpcpp::in]]`, a `counted_by` array, a value parameter); the return is not
  a `char` pointer; the tagged function is a member, virtual or generic (free functions only);
  or a tagged return is combined with a default-valued, pointer-array, derived-argument or
  record-pointer input parameter (those use dedicated wrapper writers without an owned-return
  branch; an owned output parameter has no such restriction). The out pointer is initialised to
  zero before the call, so a native function that returns without writing it (SDL's error
  paths) is neither copied nor freed. The metadata exporter writes the public managed return type of an owned return (`string`,
  from `OwnedStringTypes`, shared with the renderer) instead of the retyped raw address, and the
  free function as an optional `return_string_free_function` on the function entry and
  `string_free_function` on an owned output parameter (`native_name`, `cpp_qualified_name`; omitted
  when absent, so documents without `freed_by` are unchanged and the schema version stays 9).
  A module also exports the identity of its own release thunks as an optional top-level
  `string_free_functions` list (`native_name`, `cpp_qualified_name`, `parameter_cpp_type_name`,
  `entry_point`; local frees only, omitted when empty, schema version unchanged). The importer
  seeds them into `ImportedStringFreeFunctions` (deduplicated, with the dependency's native
  library and entry point). `OwnedStringResolver` prefers a local free and falls back to an
  imported one by native name only when there is no local candidate; an ambiguous import is a
  logged problem. The renderer emits no thunk for an imported free: the consumer's
  `[LibraryImport]` targets the dependency's library and entry point. The `Metadata` E2E fixture
  (`Dependency.hpp`/`Consumer.hpp`, `MetadataDependency.cs`) proves it: a consumer's owned return
  and output parameter are freed through `release_dependency_text`, the consumer `.g.cpp` has no
  free thunk, and the dependency's release counter rises.
  Still open: (i) a release function that no function of the dependency tags gets no thunk and
  cannot be imported; (ii) `freed_by` on record members is still rejected and imported member
  parameters do not carry `StringFreeFunction`; (iii) imported frees are not re-exported
  transitively, so a third module must load every dependency document; (iv) the E2E fixture
  cannot show cross-DLL behaviour (all modules share one native library).
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
  reason (unknown name or self, begin not a pointer, end of a different type, end not directly
  after begin, begin also `counted_by`, end itself `counted_by`/`ended_by`, an explicit
  single/out shape on a `T**` begin or any explicit shape on end, a default on begin or end
  (native, unexpressible, undefaultable or managed), no input array projection such as a
  record `T*`, template function, class template member or constructor). Element kinds:
  primitives and enums (`ReadOnlySpan<T>`/`Span<T>`), `void* const*` (`ReadOnlySpan<IntPtr>`),
  `char**` and `const char* const*` (`ReadOnlySpan<string?>`, marshalled to UTF-8 as counted), and
  record `T**`/`T* const*` (`ReadOnlySpan<T?>`, the counted handle-array projection); the end
  parameter takes begin's reshaped type. Fixtures: `Pointers/PointerRanges.hpp`. Increments 1, 2 and 3 are done (3: virtual and
  abstract non-director records work with ranges as long as the base and every override carry
  the tag, fixtures `range_shape`/`widget_query`; rbfx
  adopts it through `RbfxPointerRangesPass`, which tags `TestDrawables(start, end, inside)` as
  `ReadOnlySpan<Drawable?>` and replaced the old unsupported-API suppression). Increment 3b is
  done: director callbacks bind record, primitive, enum and address ranges (fixtures
  `range_visitor`, `int_range_visitor`, `color_range_visitor`, `address_range_visitor`). The
  native side is unchanged (one pointer per native parameter); the managed dispatch rebuilds a
  span from `begin`/`end` and the `end` argument is hidden from the override signature.
  Limitations: span elements passed to a managed override (other than strings, which are copied) are borrowed and only valid during
  the callback; a consumer `ended_by` tag on a primitive director pointer is rejected with a
  reason, because parse-time lowering has already turned it into an address value (use the
  header attribute instead; fixture `lowered_range_visitor`). Open: a future `SpanLength`
  derived kind could hide a `counted_by` count the same way; constructors are degraded today. Done: class
  template members and template functions are degraded with a reason (`CreateConcreteParameter`
  does not copy parameter metadata); the inheritance projection copies `EndedBy`,
  `DerivedArgument`, `DerivedFromParameter`, `InputPointerArray` and `WritablePointerArray`
  (fixture `range_both`); a native default on begin or end degrades the function.
