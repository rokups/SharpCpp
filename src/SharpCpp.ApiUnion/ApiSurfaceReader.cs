using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace SharpCpp.ApiUnion;

/// <summary>
/// Reads the public API of an assembly from its metadata. The output does not depend on the host:
/// it has only names, signatures, constant values and a few flags.
/// </summary>
public static class ApiSurfaceReader
{
    public static ApiSurface Read(string platform, string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var assemblyName = reader.GetString(reader.GetAssemblyDefinition().Name);
        var members = new List<ApiMember>();
        var typeProvider = new SignatureTextProvider(reader);

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (!IsPublicType(reader, type))
            {
                continue;
            }

            var typeName = GetTypeName(reader, type);
            var genericContext = new GenericContext(GetGenericParameterNames(reader, type.GetGenericParameters()), []);
            members.Add(new ApiMember(ApiMemberKind.Type, typeName, "T:" + typeName, DescribeType(reader, type, typeProvider, genericContext)));

            foreach (var fieldHandle in type.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if (!IsVisible(field.Attributes & FieldAttributes.FieldAccessMask))
                {
                    continue;
                }

                var fieldType = field.DecodeSignature(typeProvider, genericContext);
                var name = reader.GetString(field.Name);
                var details = new StringBuilder();
                if ((field.Attributes & FieldAttributes.Static) != 0) details.Append("static;");
                if ((field.Attributes & FieldAttributes.Literal) != 0) details.Append("const=").Append(FormatConstant(reader, field.GetDefaultValue())).Append(';');
                if ((field.Attributes & FieldAttributes.InitOnly) != 0) details.Append("readonly;");
                members.Add(new ApiMember(ApiMemberKind.Field, typeName, $"F:{typeName}.{name}:{fieldType}", details.ToString()));
            }

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (!IsVisible(method.Attributes & MethodAttributes.MemberAccessMask))
                {
                    continue;
                }

                var methodGenericContext = new GenericContext(genericContext.TypeParameters, GetGenericParameterNames(reader, method.GetGenericParameters()));
                var signature = method.DecodeSignature(typeProvider, methodGenericContext);
                var name = reader.GetString(method.Name);
                var arity = methodGenericContext.MethodParameters.Length > 0 ? $"``{methodGenericContext.MethodParameters.Length}" : string.Empty;
                // A conversion operator is overloaded by its return type.
                var returnSuffix = name is "op_Implicit" or "op_Explicit" ? ":" + signature.ReturnType : string.Empty;
                var key = $"M:{typeName}.{name}{arity}({string.Join(",", signature.ParameterTypes)}){returnSuffix}";
                var details = new StringBuilder();
                details.Append("returns=").Append(signature.ReturnType).Append(';');
                if ((method.Attributes & MethodAttributes.Static) != 0) details.Append("static;");
                if ((method.Attributes & MethodAttributes.Virtual) != 0) details.Append("virtual;");
                if ((method.Attributes & MethodAttributes.Abstract) != 0) details.Append("abstract;");
                members.Add(new ApiMember(ApiMemberKind.Method, typeName, key, details.ToString()));
            }

            foreach (var propertyHandle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                var getterVisible = !accessors.Getter.IsNil && IsVisible(reader.GetMethodDefinition(accessors.Getter).Attributes & MethodAttributes.MemberAccessMask);
                var setterVisible = !accessors.Setter.IsNil && IsVisible(reader.GetMethodDefinition(accessors.Setter).Attributes & MethodAttributes.MemberAccessMask);
                if (!getterVisible && !setterVisible)
                {
                    continue;
                }

                var signature = property.DecodeSignature(typeProvider, genericContext);
                var name = reader.GetString(property.Name);
                var indexer = signature.ParameterTypes.Length > 0 ? $"({string.Join(",", signature.ParameterTypes)})" : string.Empty;
                var details = (getterVisible ? "get;" : string.Empty) + (setterVisible ? "set;" : string.Empty);
                members.Add(new ApiMember(ApiMemberKind.Property, typeName, $"P:{typeName}.{name}{indexer}:{signature.ReturnType}", details));
            }

            foreach (var eventHandle in type.GetEvents())
            {
                var eventDefinition = reader.GetEventDefinition(eventHandle);
                var accessors = eventDefinition.GetAccessors();
                if (accessors.Adder.IsNil || !IsVisible(reader.GetMethodDefinition(accessors.Adder).Attributes & MethodAttributes.MemberAccessMask))
                {
                    continue;
                }

                var name = reader.GetString(eventDefinition.Name);
                var eventType = typeProvider.GetTypeFromHandle(eventDefinition.Type, genericContext);
                members.Add(new ApiMember(ApiMemberKind.Event, typeName, $"E:{typeName}.{name}:{eventType}", string.Empty));
            }
        }

        return new ApiSurface(platform, assemblyName, members);
    }

    private static bool IsPublicType(MetadataReader reader, TypeDefinition type)
    {
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        if (visibility == TypeAttributes.Public)
        {
            return true;
        }

        if (visibility is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem)
        {
            return IsPublicType(reader, reader.GetTypeDefinition(type.GetDeclaringType()));
        }

        return false;
    }

    private static bool IsVisible(FieldAttributes access)
        => access is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem;

    private static bool IsVisible(MethodAttributes access)
        => access is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    private static string GetTypeName(MetadataReader reader, TypeDefinition type)
    {
        var name = reader.GetString(type.Name);
        if (type.IsNested)
        {
            return GetTypeName(reader, reader.GetTypeDefinition(type.GetDeclaringType())) + "+" + name;
        }

        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static ImmutableArray<string> GetGenericParameterNames(MetadataReader reader, GenericParameterHandleCollection handles)
        => handles.Select(handle => reader.GetString(reader.GetGenericParameter(handle).Name)).ToImmutableArray();

    private static string DescribeType(MetadataReader reader, TypeDefinition type, SignatureTextProvider typeProvider, GenericContext genericContext)
    {
        var details = new StringBuilder();
        var attributes = type.Attributes;
        if ((attributes & TypeAttributes.Interface) != 0) details.Append("interface;");
        else if ((attributes & TypeAttributes.Abstract) != 0 && (attributes & TypeAttributes.Sealed) != 0) details.Append("static;");
        else if ((attributes & TypeAttributes.Abstract) != 0) details.Append("abstract;");
        else if ((attributes & TypeAttributes.Sealed) != 0) details.Append("sealed;");

        if (!type.BaseType.IsNil)
        {
            details.Append("base=").Append(typeProvider.GetTypeFromHandle(type.BaseType, genericContext)).Append(';');
        }

        var interfaces = type.GetInterfaceImplementations()
            .Select(handle => typeProvider.GetTypeFromHandle(reader.GetInterfaceImplementation(handle).Interface, genericContext))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        if (interfaces.Count > 0)
        {
            details.Append("implements=").Append(string.Join("|", interfaces)).Append(';');
        }

        // The underlying type of an enum is the type of its instance field value__.
        foreach (var fieldHandle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.Static) == 0 && reader.StringComparer.Equals(field.Name, "value__"))
            {
                details.Append("underlying=").Append(field.DecodeSignature(typeProvider, genericContext)).Append(';');
            }
        }

        return details.ToString();
    }

    private static string FormatConstant(MetadataReader reader, ConstantHandle handle)
    {
        if (handle.IsNil)
        {
            return "null";
        }

        var constant = reader.GetConstant(handle);
        var blob = reader.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Boolean => blob.ReadBoolean().ToString(),
            ConstantTypeCode.Char => ((int)blob.ReadChar()).ToString(),
            ConstantTypeCode.SByte => blob.ReadSByte().ToString(),
            ConstantTypeCode.Byte => blob.ReadByte().ToString(),
            ConstantTypeCode.Int16 => blob.ReadInt16().ToString(),
            ConstantTypeCode.UInt16 => blob.ReadUInt16().ToString(),
            ConstantTypeCode.Int32 => blob.ReadInt32().ToString(),
            ConstantTypeCode.UInt32 => blob.ReadUInt32().ToString(),
            ConstantTypeCode.Int64 => blob.ReadInt64().ToString(),
            ConstantTypeCode.UInt64 => blob.ReadUInt64().ToString(),
            ConstantTypeCode.Single => blob.ReadSingle().ToString("R"),
            ConstantTypeCode.Double => blob.ReadDouble().ToString("R"),
            ConstantTypeCode.String => "\"" + blob.ReadUTF16(blob.Length) + "\"",
            ConstantTypeCode.NullReference => "null",
            _ => "?"
        };
    }
}

/// <summary>Generic parameter names of the enclosing type and method, for signature text.</summary>
public sealed record GenericContext(ImmutableArray<string> TypeParameters, ImmutableArray<string> MethodParameters);

/// <summary>Renders a metadata type signature as a C#-like string.</summary>
public sealed class SignatureTextProvider(MetadataReader reader) : ISignatureTypeProvider<string, GenericContext>
{
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.IntPtr => "nint",
        PrimitiveTypeCode.UIntPtr => "nuint",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        _ => typeCode.ToString()
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        if (type.IsNested)
        {
            return GetTypeFromDefinition(reader, type.GetDeclaringType(), rawTypeKind) + "+" + name;
        }

        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "+" + name;
        }

        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, GenericContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetTypeFromHandle(EntityHandle handle, GenericContext genericContext) => handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(reader, genericContext, (TypeSpecificationHandle)handle, 0),
        _ => handle.Kind.ToString()
    };

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => genericType + "<" + string.Join(",", typeArguments) + ">";

    public string GetGenericTypeParameter(GenericContext genericContext, int index)
        => index < genericContext.TypeParameters.Length ? genericContext.TypeParameters[index] : "!" + index;

    public string GetGenericMethodParameter(GenericContext genericContext, int index)
        => index < genericContext.MethodParameters.Length ? genericContext.MethodParameters[index] : "!!" + index;

    public string GetFunctionPointerType(MethodSignature<string> signature)
        => "delegate*<" + string.Join(",", signature.ParameterTypes.Add(signature.ReturnType)) + ">";

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        => unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";

    public string GetPinnedType(string elementType) => elementType + " pinned";
}
