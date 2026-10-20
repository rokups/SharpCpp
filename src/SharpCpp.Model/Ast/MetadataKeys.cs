namespace SharpCpp.Model.Ast;

public static class MetadataKeys
{
    public const string FunctionPointerField = "function_pointer_field";

    /// <summary>
    /// A value field whose ReturnMarshaller is owning. When the field does not borrow record storage,
    /// the native getter allocates an owned copy, so the managed getter owns the result.
    /// </summary>
    public const string OwnedValueFieldGetter = "owned_value_field_getter";
    public const string BitField = "bit_field";
    public const string ExposeNativeFunctionPointer = "expose_native_function_pointer";
    public const string CanonicalCppIdentity = "canonical_cpp_identity";
    public const string ManagedTypeIdentity = "managed_type_identity";
    public const string FixedArrayRecord = "fixed_array_record";
    public const string NativeArrayRecord = "native_array_record";
    public const string DynamicArrayRecord = "dynamic_array_record";
    public const string Marshaller = "marshaller";
    public const string AccessorProperty = "accessor_property";
    public const string IgnoreReason = "ignore_reason";
    public const string SilentExclusion = "silent_exclusion";

    // Set on a secondary-base record whose managed interface carries a hidden handle accessor,
    // so SecondaryBase* parameters and returns can be typed as that interface.
    public const string SecondaryBaseView = "secondary_base_view";
    // Set on an override that was ignored because its covariant return type cannot satisfy the
    // managed interface of a secondary base; the base declaration is projected in its place.
    public const string CovariantSecondaryBaseOverride = "covariant_secondary_base_override";
    // Set on a projected secondary-base method that was bound non-virtually because a covariant
    // override shadows it; it keeps the declared return type so it still implements the interface.
    public const string CovariantProjectedBaseMethod = "covariant_projected_base_method";
    public const string ProjectedSecondaryBaseMethod = "projected_secondary_base_method";
    // Set on a non-virtual clone of a secondary-base interface member that a derived record hides
    // with another return type; the value is the SymbolId of the record whose interface declares
    // the member. The class renders it as an explicit implementation of that interface member.
    public const string ExplicitInterfaceImplementation = "explicit_interface_implementation";
    public const string Warnings = "warnings";
    public const string DocumentationComment = "documentation_comment";
    public const string DeclarationDisplayText = "declaration_display_text";
    public const string ExplicitGenericInstantiations = "explicit_generic_instantiations";
    public const string ForwardDeclaration = "forward_declaration";
    public const string OpaqueNativeHandle = "opaque_native_handle";
    public const string NativeInterfaceWrapper = "native_interface_wrapper";
    public const string NativeInterfaceQuery = "native_interface_query";
    public const string NativeInterfaceReleaseMethod = "native_interface_release_method";
    public const string PlaceholderRecord = "placeholder_record";
    // Set (string[]) on a placeholder record by the parser. The spelled, canonical and normalized
    // canonical text of the first clang types that mapped to the placeholder. Diagnostics only.
    public const string PlaceholderTypeOrigins = "placeholder_type_origins";
    public const string SuppressEmission = "suppress_emission";
    public const string HasNativeDefinition = "has_native_definition";
    // Set (bool) on a function, a method or a static data member by the parser. True when the
    // declaration needs a symbol from a different binary: the translation unit has no definition,
    // not even an inline one that follows the declaration.
    public const string DeclaredOnly = "declared_only";
    public const string NativeCallableDeleted = "native_callable_deleted";
    public const string NonPublicDestructor = "non_public_destructor";
    // Set (string[]) on a record by the parser. The C++ qualified names of the functions that the
    // destructor of the record references and that have no definition in the parsed translation
    // unit: the destructors of the record, its bases and its by-value fields, and the destructors
    // and call operators of the type arguments of a template field. A consumer compares the names
    // with the symbols that the linked library exports. Absent when the list is empty.
    public const string DestructorLinkDependencies = "destructor_link_dependencies";
    // Set (string[]) on a record by the parser. The C++ qualified names of the virtual functions in
    // the virtual table of the record (the final overriders, of any access) that have no definition
    // in the parsed translation unit. Absent when the list is empty.
    public const string VirtualTableLinkDependencies = "virtual_table_link_dependencies";
    // Set (string[]) on a record by the parser, only when the default constructor of the record is
    // implicit or defaulted. The C++ qualified names of the default constructors that the
    // implicit one references and that have no definition in the parsed translation unit: the
    // default constructors of its bases and of its by-value fields. Absent when the list is empty.
    public const string DefaultConstructorLinkDependencies = "default_constructor_link_dependencies";
    // Set (long, bytes) on a record by the parser: the size and the alignment of the complete
    // record type on the parse target. Absent when the record is not complete, for example a
    // forward declaration or a class template.
    public const string RecordSize = "record_size";
    public const string RecordAlignment = "record_alignment";
    // Set (long, bits) on a non-static field by the parser: the offset of the field in its record
    // on the parse target. Absent when the layout is not known.
    public const string FieldOffset = "field_offset";
    public const string UnexpressibleDefault = "unexpressible_default";
    public const string MarshallerExposedMethod = "marshaller_exposed_method";
    public const string MarshallerConfiguredRecord = "marshaller_configured_record";
    // Set on a marshaller-backed class template: each instantiation renders as its own class.
    public const string MarshallerFlattenInstantiations = "marshaller_flatten_instantiations";
    // Set on a flattened instantiation record: the symbol id of its class template record.
    public const string FlattenedInstantiationOf = "flattened_instantiation_of";
    // Set on an exposed method that returns bool: false throws InvalidOperationException with this message.
    public const string MarshallerThrowOnFalse = "marshaller_throw_on_false";
    public const string UndefaultableParam = "undefaultable_param";
    public const string ManagedBindingProjection = "managed_binding_projection";
    public const string ManagedMarshaller = "managed_marshaller";
    public const string ManagedWrapperAssembly = "managed_wrapper_assembly";
    public const string ManagedWrapperProviderKind = "managed_wrapper_provider_kind";
    public const string ManagedWrapperProviderNamespace = "managed_wrapper_provider_namespace";
    public const string ManagedWrapperCopyable = "managed_wrapper_copyable";
    // Set (string) on an enum that an external header declares and that an owned alias names.
    // The value is the C++ qualified name of the alias. The alias scope sets the managed namespace.
    public const string ExternalTypeAlias = "external_type_alias";
    public const string ExplicitManagedRename = "explicit_managed_rename";
    public const string ManagedStaticInterfaces = "managed_static_interfaces";
    public const string ManagedStaticInterfacesAbstract = "managed_static_interfaces_abstract";
    // Set (bool) by the metadata importer on a record that a different module wraps as a native
    // handle (a NativeObject subclass). Pointers and references to it stay handle types in the
    // importing module; they are not projected as configured value types.
    public const string ImportedHandleRecord = "imported_handle_record";
    // Set (bool) by SilentlyExcludeUnusedSharedWrappersPass on an imported handle record that the
    // importing module uses as a handle type. The module emits its own handle marshaller for it,
    // because the marshaller of the module that wraps the record is internal to that assembly.
    public const string ImportedHandleMarshaller = "imported_handle_marshaller";
    public const string NativeFieldAccessExpression = "native_field_access_expression";
    public const string NativeDefaultValueExpression = "native_default_value_expression";
    public const string NativeDefaultArgumentAdapter = "native_default_argument_adapter";
    public const string InputPointerArray = "input_pointer_array";
    // Set (bool) on an input `char**` array or range by the `pointers` shape. Such a parameter keeps
    // the raw `ReadOnlySpan<nint>` binding (the caller owns the array and the strings) instead of
    // the default `ReadOnlySpan<string?>` the wrapper marshals and frees. Absent on every default path.
    public const string RawStringPointerArray = "raw_string_pointer_array";
    // Set (bool) on an input `char**` string array by `[[sharpcpp::null_terminated]]` or a consumer
    // pass: native reads the list up to a NULL element, with or without a count. The managed wrapper
    // passes one extra zero element after the strings (the terminator is not counted). Valid only
    // on a decoded input string array (PointerPointerShapeResolver.Apply ignores the function otherwise);
    // an empty span still passes a null pointer.
    public const string NullTerminatedStringArray = "null_terminated_string_array";
    public const string WritablePointerArray = "writable_pointer_array";
    // Value: native name of the capacity parameter that bounds this record output buffer.
    public const string RecordBufferCapacityParameter = "record_buffer_capacity_parameter";
    // Value: minimum element count (int) a span parameter must have; the managed wrapper throws
    // ArgumentException before the native call when the span is shorter.
    public const string MinimumSpanLength = "minimum_span_length";
    // Value: "array", "single" or "pointers" (PointerPointerShapes). Selects how a pointer-to-pointer
    // parameter to char or a record is bound: as an input array (a span of strings or pointers),
    // as one output value, or (char** only) as an input array of raw caller-owned UTF-8 pointers.
    // The parser sets the default from an adjacent value count and the [[sharpcpp::in]] /
    // [[sharpcpp::out]] / [[sharpcpp::shape(...)]] attributes; a consumer overwrites it before the
    // ResolvePointerPointerShapes pipeline step (any configuration hook, or right after parsing).
    public const string PointerPointerShape = "pointer_pointer_shape";
    // Value: the shape the parser tagged as a default (not an explicit [[sharpcpp::in]] /
    // [[sharpcpp::out]]). A shape equal to it is treated as not chosen, so an explicit
    // CountedBy can still turn the parameter into an array; any other shape is a choice.
    public const string PointerPointerDefaultShape = "pointer_pointer_default_shape";
    // Value: native name of the integer value parameter that holds the element count of this
    // array parameter (`[[sharpcpp::counted_by(n)]]`, or set by a consumer). It bypasses the
    // count-name heuristic and the adjacency/ownership rules; one count may serve several
    // arrays. On a char** or record T** it selects the array shape unless an explicit shape is
    // present; on a primitive or handle pointer it makes the parameter an input array.
    public const string CountedBy = "counted_by";
    // Value: native name of the integer value parameter that counts this input `char**` array
    // (decoded to ReadOnlySpan<string?>): the explicit CountedBy count, or else the adjacent
    // count parameter the shape default pairs it with. Set by PointerPointerShapeResolver; absent
    // when the array has no identifiable count or is an ended_by range. A director callback uses
    // it to size the received span.
    public const string StringArrayCount = "string_array_count";
    // Value: native name of the end parameter of a `(T* begin, T* end)` pair
    // (`[[sharpcpp::ended_by(end)]]` on begin, mirroring Clang `__ended_by`, or set by a
    // consumer). Binds the pair as ONE managed span parameter: `ReadOnlySpan<T>` for a const
    // pointee, `Span<T>` for a mutable one. The wrapper passes begin = the pinned pointer and
    // end = begin + span.Length; an empty span passes null/null, which is a valid empty range.
    // Element kinds: primitives and enums (`ReadOnlySpan<T>`/`Span<T>`), record `T**`
    // (`ReadOnlySpan<T?>`), `char**` and `void* const*` (nint/IntPtr spans). Every override of a
    // virtual method must carry the tag too, or its signature differs from the base. Director
    // callbacks bind the range as a span of borrowed elements (record, primitive, enum and address
    // elements, and `char**`/`void* const*` as IntPtr spans). There is no name-based default, and
    // the tag is valid on parameters only. An invalid tag ignores the function with a logged reason (PointerPointerShapeResolver.Apply).
    public const string EndedBy = "ended_by";
    // Value: a DerivedArgumentKinds string. Set on a parameter whose native argument is computed
    // from another parameter, so it has no managed counterpart: it stays in Parameters at its
    // native index (invocation plans, variants and dispatch are index-aligned) and only public
    // managed signatures skip it. A future `SpanLength` kind could hide a counted_by count the
    // same way.
    public const string DerivedArgument = "derived_argument";
    // Value: native name of the parameter a derived argument is computed from.
    public const string DerivedFromParameter = "derived_from_parameter";
    // Set by parse-time lowering of primitive pointer/reference parameters of director virtuals
    // (they cross the callback as an address value). A consumer tag applied after that point
    // cannot recover the pointer, so the ended_by resolver reports it instead.
    public const string DirectorLoweredPointer = "director_lowered_pointer";
    // Set on an out char** parameter that is bound as `out string?`: the wrapper copies the UTF-8
    // text and never frees the native pointer (a borrowed string).
    public const string BorrowedStringOutput = "borrowed_string_output";
    // Value: native name of the free function that releases a caller-owned string
    // (`[[sharpcpp::freed_by(SDL_free)]]`, or set by a consumer). Valid on an output `char**`
    // ParameterBinding or on a FunctionBinding that returns `char*`/`const char*`; the wrapper
    // copies the UTF-8 text into a managed string and then releases the native pointer. OwnedStringResolver
    // validates the tag and, on success, records StringFreeFunction; the renderer reads only that.
    // C headers parsed as C17 cannot carry `[[...]]`; consumers must set this key through the
    // metadata path there. An invalid tag ignores the function with a logged reason.
    public const string FreedBy = "freed_by";
    // Value: StringFreeFunction. Set by OwnedStringResolver on a validated FreedBy node.
    public const string StringFreeFunction = "string_free_function";
    // Set (bool) on a FunctionBinding whose canonical return type is a pointer to char
    // (`char*` or `const char*`, not `char**`). Lets the resolver validate a FreedBy return tag.
    public const string CharPointerReturn = "char_pointer_return";
    // Value: "; "-joined C++ spellings of the explicit full specializations declared for a class
    // template record. Their members are independent of the primary template, so primary-template
    // field accessors cannot be rendered against them.
    public const string ExplicitFullSpecializations = "explicit_full_specializations";
}

/// <summary>Values of <see cref="MetadataKeys.PointerPointerShape"/>.</summary>
public static class PointerPointerShapes
{
    public const string Array = "array";
    public const string Single = "single";
    /// <summary>`char**` only: an input array of raw UTF-8 pointers the caller owns (`ReadOnlySpan&lt;nint&gt;`).</summary>
    public const string Pointers = "pointers";
}

/// <summary>
/// The validated free function of an owned string (<see cref="MetadataKeys.StringFreeFunction"/>).
/// <paramref name="NativeName"/> is the C++ name used in the thunk export; <paramref name="CppQualifiedName"/>
/// is the call target and <paramref name="ParameterCppTypeName"/> the C++ type of its one parameter.
/// <paramref name="Import"/> is <see langword="null"/> for a local free, whose library and entry point are
/// derived from the compilation at render time; an imported free never gets a thunk in this module.
/// </summary>
public sealed record StringFreeFunction(
    string NativeName,
    string CppQualifiedName,
    string ParameterCppTypeName,
    StringFreeFunctionImport? Import = null)
{
    /// <summary>True when the release thunk lives in a dependency module (see <see cref="Import"/>).</summary>
    public bool IsImported => Import is not null;
}

/// <summary>
/// The release-thunk identity of an imported string free function: the native library that exports it
/// and its entry point name there.
/// </summary>
public sealed record StringFreeFunctionImport(string NativeLibraryName, string EntryPointName);

public sealed record NativeInterfaceQueryAnnotation(string SourceRecordCppQualifiedName, string InterfaceIdExpression);

public enum AccessorPropertyKind
{
    Getter = 0,
    Setter = 1
}

public sealed record AccessorPropertyAnnotation(string PropertyName, AccessorPropertyKind Kind);

/// <summary>Values of <see cref="MetadataKeys.DerivedArgument"/>.</summary>
public static class DerivedArgumentKinds
{
    /// <summary>The end of a `(begin, end)` range: the begin argument plus the span length.</summary>
    public const string PointerRangeEnd = "pointer_range_end";
}

/// <summary>
/// Public managed spelling of an owned string return (<see cref="MetadataKeys.StringFreeFunction"/>): the
/// wrapper exposes <c>string?</c>, and the metadata document records <c>string</c> (it never spells
/// nullability for configured types). Shared by the renderer and the metadata exporter.
/// </summary>
public static class OwnedStringTypes
{
    public const string ManagedTypeName = "string";
    public const string NullableManagedTypeName = ManagedTypeName + "?";

    /// <summary>
    /// The public return type of a function whose <c>char*</c> return is an owned string. The function's
    /// <c>ReturnType</c> is the raw address the P/Invoke import returns, not what the wrapper exposes.
    /// </summary>
    public static BindingType CreatePublicReturnType(string cppReturnTypeName)
        => BindingType.CreateConfigured(cppReturnTypeName, ManagedTypeName);
}
