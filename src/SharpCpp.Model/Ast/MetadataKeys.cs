namespace SharpCpp.Model.Ast;

public static class MetadataKeys
{
    public const string FunctionPointerField = "function_pointer_field";
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
    public const string SuppressEmission = "suppress_emission";
    public const string HasNativeDefinition = "has_native_definition";
    public const string NativeCallableDeleted = "native_callable_deleted";
    public const string NonPublicDestructor = "non_public_destructor";
    public const string UnexpressibleDefault = "unexpressible_default";
    public const string MarshallerExposedMethod = "marshaller_exposed_method";
    public const string MarshallerConfiguredRecord = "marshaller_configured_record";
    public const string UndefaultableParam = "undefaultable_param";
    public const string ManagedBindingProjection = "managed_binding_projection";
    public const string ManagedMarshaller = "managed_marshaller";
    public const string ManagedWrapperAssembly = "managed_wrapper_assembly";
    public const string ManagedWrapperProviderKind = "managed_wrapper_provider_kind";
    public const string ManagedWrapperProviderNamespace = "managed_wrapper_provider_namespace";
    public const string ManagedWrapperCopyable = "managed_wrapper_copyable";
    public const string ExplicitManagedRename = "explicit_managed_rename";
    public const string ManagedStaticInterfaces = "managed_static_interfaces";
    public const string ManagedStaticInterfacesAbstract = "managed_static_interfaces_abstract";
    public const string NativeFieldAccessExpression = "native_field_access_expression";
    public const string NativeDefaultValueExpression = "native_default_value_expression";
    public const string NativeDefaultArgumentAdapter = "native_default_argument_adapter";
    public const string InputPointerArray = "input_pointer_array";
    public const string WritablePointerArray = "writable_pointer_array";
    // Value: native name of the capacity parameter that bounds this record output buffer.
    public const string RecordBufferCapacityParameter = "record_buffer_capacity_parameter";
    // Value: minimum element count (int) a span parameter must have; the managed wrapper throws
    // ArgumentException before the native call when the span is shorter.
    public const string MinimumSpanLength = "minimum_span_length";
    // Value: "array" or "single" (PointerPointerShapes). Selects how a pointer-to-pointer
    // parameter to char or a record is bound: as an input array (a span of strings or pointers)
    // or as one output value. The parser sets the default from an adjacent value count and the
    // [[sharpcpp::in]] / [[sharpcpp::out]] attributes; a consumer overwrites it before the
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
}

/// <summary>
/// The validated free function of an owned string (<see cref="MetadataKeys.StringFreeFunction"/>).
/// <paramref name="NativeName"/> is the C++ name used in the thunk export; <paramref name="CppQualifiedName"/>
/// is the call target and <paramref name="ParameterCppTypeName"/> the C++ type of its one parameter.
/// </summary>
public sealed record StringFreeFunction(string NativeName, string CppQualifiedName, string ParameterCppTypeName);

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
