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
    // Set on an out char** parameter that is bound as `out string?`: the wrapper copies the UTF-8
    // text and never frees the native pointer (a borrowed string).
    public const string BorrowedStringOutput = "borrowed_string_output";
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

public sealed record NativeInterfaceQueryAnnotation(string SourceRecordCppQualifiedName, string InterfaceIdExpression);

public enum AccessorPropertyKind
{
    Getter = 0,
    Setter = 1
}

public sealed record AccessorPropertyAnnotation(string PropertyName, AccessorPropertyKind Kind);
