using SharpCpp.Model.Ast;
using SharpCpp.Model.Utilities;

namespace SharpCpp.Rendering;

public static partial class BindingOutputRenderer
{
    private static string RenderGenericRecord(BindingCompilation compilation, RecordBinding record)
    {
        ValidateGenericRecordSlice(record);

        var writer = new CodeWriter();
        var constructors = record.Members.OfType<FunctionBinding>().Where(static function => function.IsConstructor).OrderBy(static function => function.Parameters.Count).ToArray();
        var instanceMethods = record.Members.OfType<FunctionBinding>().Where(static function => !function.IsConstructor && !function.IsStatic && function.OperatorToken is null && !function.IsConversionOperator).OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
        var genericInstanceMethods = instanceMethods.Where(static function => function.IsGeneric).ToArray();
        var concreteInstanceMethods = instanceMethods.Where(static function => !function.IsGeneric).ToArray();
        var staticMethods = record.Members.OfType<FunctionBinding>().Where(static function => function.IsStatic && function.OperatorToken is null && !function.IsConversionOperator).OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
        var instantiations = record.SupportedGenericInstantiations.OrderBy(static instantiation => string.Join("|", instantiation.TypeArguments.Select(static argument => argument.CppTypeName)), StringComparer.Ordinal).ToArray();

        WriteDocumentationComment(writer, record);
        writer.Write($$"""
            public partial class {{GetGenericRecordDeclarationName(record)}} : global::SharpCpp.Support.NativeObject
            {
            internal static readonly global::SharpCpp.Support.DefaultNativeInstanceCache s_instanceCache = new();

            protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => s_instanceCache;

            internal {{record.ManagedName}}(nint handle, bool ownsHandle = false)
                : base(handle, ownsHandle)
            {
            if (!IsInvalid)
                s_instanceCache.Register(this);
            }

            public nint Handle => DangerousGetHandle();
            public bool OwnsHandle => OwnsNativeHandle;
            public bool IsNull => IsClosed || IsInvalid;

            internal static {{GetGenericRecordDeclarationName(record)}} CreateManagedWrapper(nint handle, bool ownsHandle)
            {
            if (handle == 0)
                return new {{GetGenericRecordDeclarationName(record)}}(0, ownsHandle);

            if (s_instanceCache.TryGetExisting(handle, ownsHandle, out var existing) && existing is {{GetGenericRecordDeclarationName(record)}} cached)
                return cached;

            return new {{GetGenericRecordDeclarationName(record)}}(handle, ownsHandle);
            }
            """);

        foreach (var constructor in constructors)
        {
            writer.WriteLine();
            writer.Write(RenderGenericManagedConstructor(compilation, record, constructor, instantiations));
        }

        foreach (var method in concreteInstanceMethods)
        {
            writer.WriteLine();
            writer.Write(RenderGenericManagedInstanceMethod(compilation, record, method, instantiations));
        }

        foreach (var method in genericInstanceMethods)
        {
            writer.WriteLine();
            writer.Write(RenderGenericManagedGenericMethod(compilation, record, method));
        }

        foreach (var method in staticMethods)
        {
            writer.WriteLine();
            writer.Write(RenderGenericManagedStaticMethod(compilation, record, method, instantiations));
        }

        writer.WriteLine();
        writer.Write(RenderGenericDeleteHelper(compilation, record, instantiations));

        writer.WriteLine();
        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static IEnumerable<string> CollectGenericImportDeclarations(BindingCompilation compilation, RecordBinding record)
    {
        var constructors = record.Members.OfType<FunctionBinding>().Where(static function => function.IsConstructor).OrderBy(static function => function.Parameters.Count).ToArray();
        var instanceMethods = record.Members.OfType<FunctionBinding>().Where(static function => !function.IsConstructor && !function.IsStatic && function.OperatorToken is null && !function.IsConversionOperator).OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
        var concreteInstanceMethods = instanceMethods.Where(static function => !function.IsGeneric).ToArray();
        var staticMethods = record.Members.OfType<FunctionBinding>().Where(static function => function.IsStatic && function.OperatorToken is null && !function.IsConversionOperator).OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
        var instantiations = record.SupportedGenericInstantiations.OrderBy(static instantiation => string.Join("|", instantiation.TypeArguments.Select(static argument => argument.CppTypeName)), StringComparer.Ordinal).ToArray();

        foreach (var constructor in constructors)
        {
            foreach (var instantiation in instantiations)
            {
                yield return RenderGenericConstructorImportDeclaration(compilation, record, constructor, instantiation);
            }
        }

        foreach (var method in concreteInstanceMethods)
        {
            foreach (var instantiation in instantiations)
            {
                yield return RenderGenericInstanceImportDeclaration(compilation, record, method, instantiation);
            }
        }

        foreach (var method in staticMethods)
        {
            foreach (var instantiation in instantiations)
            {
                yield return RenderGenericStaticImportDeclaration(compilation, record, method, instantiation);
            }
        }

        foreach (var instantiation in instantiations)
        {
            yield return RenderGenericDeleteImportDeclaration(compilation, record, instantiation);
        }
    }

    private static void ValidateGenericRecordSlice(RecordBinding record)
    {
        if (record.IsDirector || record.IsPolymorphic || record.Bases.Count > 0)
        {
            throw new NotSupportedException($"Generic record '{record.ManagedName}' is limited to non-polymorphic, non-director, non-inheriting surface in this slice.");
        }

        if (record.Members.OfType<FieldBinding>().Any())
        {
            throw new NotSupportedException($"Generic record '{record.ManagedName}' does not support field projection in this slice.");
        }

        if (record.Members.OfType<FunctionBinding>().Any(static function => function.OperatorToken is not null || function.IsConversionOperator))
        {
            throw new NotSupportedException($"Generic record '{record.ManagedName}' does not support operators in this slice.");
        }

        foreach (var instantiation in record.SupportedGenericInstantiations)
        {
            if (instantiation.TypeArguments.Any(argument => argument.Kind is not (BindingTypeKind.Primitive or BindingTypeKind.Enum)))
            {
                throw new NotSupportedException($"Generic record '{record.ManagedName}' currently supports only primitive/enum concrete type arguments.");
            }
        }
    }

    private static string GetGenericParameterList(RecordBinding record)
    {
        return record.GenericParameters.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", record.GenericParameters.Select(static parameter => parameter.Name))}>";
    }

    private static string GetGenericRecordDeclarationName(RecordBinding record)
    {
        return $"{record.ManagedName}{GetGenericParameterList(record)}";
    }

    private static string GetGenericImportAccessor(BindingCompilation compilation)
    {
        return $"{compilation.ManagedClassName}.__PInvoke";
    }

    private static string RenderGenericManagedConstructor(BindingCompilation compilation, RecordBinding record, FunctionBinding function, IReadOnlyList<RecordGenericInstantiationBinding> instantiations)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var parameterList = string.Join(", ", function.Parameters.Select(RenderManagedPublicParameter));

        writer.Write($$"""
            public {{record.ManagedName}}({{parameterList}})
                : this({{GetGenericConstructorFactoryName(function)}}({{string.Join(", ", function.Parameters.Select(static parameter => parameter.ManagedName))}}), true)
            {
            }

            private static nint {{GetGenericConstructorFactoryName(function)}}({{parameterList}})
            {
            {{RenderGenericManagedDispatchBranches(compilation, record, function, instantiations, includeSelf: false)}}
            throw new NotSupportedException("SharpCpp does not support the requested generic argument combination for '{{record.ManagedName}}'.");
            }
            """);
        return writer.ToString();
    }

    private static string RenderGenericManagedInstanceMethod(BindingCompilation compilation, RecordBinding record, FunctionBinding function, IReadOnlyList<RecordGenericInstantiationBinding> instantiations)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var parameterList = string.Join(", ", function.Parameters.Select(RenderManagedPublicParameter));
        var returnTypeName = GetManagedPublicReturnTypeName(function.ReturnType);

        writer.Write($$"""
            public {{GetManagedInstanceMethodModifier(compilation, record, function)}}{{returnTypeName}} {{function.ManagedName}}({{parameterList}})
            {
            {{RenderGenericManagedDispatchBranches(compilation, record, function, instantiations, includeSelf: true)}}
            throw new NotSupportedException("SharpCpp does not support the requested generic argument combination for '{{function.ManagedName}}'.");
            }
            """);
        return writer.ToString();
    }

    private static string RenderGenericManagedStaticMethod(BindingCompilation compilation, RecordBinding record, FunctionBinding function, IReadOnlyList<RecordGenericInstantiationBinding> instantiations)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var parameterList = string.Join(", ", function.Parameters.Select(RenderManagedPublicParameter));
        var returnTypeName = GetManagedPublicReturnTypeName(function.ReturnType);

        writer.Write($$"""
            public static {{returnTypeName}} {{function.ManagedName}}({{parameterList}})
            {
            {{RenderGenericManagedDispatchBranches(compilation, record, function, instantiations, includeSelf: false)}}
            throw new NotSupportedException("SharpCpp does not support the requested generic argument combination for '{{function.ManagedName}}'.");
            }
            """);
        return writer.ToString();
    }

    private static string RenderGenericManagedGenericMethod(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var methodTypeParams = string.Join(", ", function.GenericParameters.Select(static param => param.Name));
        var returnTypeName = GetGenericManagedReturnType(function.ReturnType);
        var parameterList = string.Join(", ", function.Parameters.Select(RenderManagedPublicParameter));

        writer.Write($$"""
            public {{returnTypeName}} {{function.ManagedName}}<{{methodTypeParams}}>({{parameterList}})
            {
            throw new NotSupportedException("SharpCpp does not yet support generic methods inside generic records for type argument combination <{{methodTypeParams}}>.");
            }
            """);
        return writer.ToString();
    }

    private static string GetGenericManagedReturnType(BindingType type)
    {
        if (type.IsGenericParameter)
        {
            return type.GenericParameterName ?? "object";
        }

        return GetManagedPublicReturnTypeName(type);
    }

    private static string RenderGenericManagedDispatchBranches(BindingCompilation compilation, RecordBinding record, FunctionBinding function, IReadOnlyList<RecordGenericInstantiationBinding> instantiations, bool includeSelf)
    {
        var writer = new CodeWriter();

        foreach (var instantiation in instantiations)
        {
            var concreteParameters = function.Parameters.Select(parameter => SubstituteGenericType(record, parameter.Type, instantiation)).ToArray();
            var concreteReturnType = SubstituteGenericType(record, function.ReturnType, instantiation);
            var invocationArguments = new List<string>();
            if (includeSelf)
            {
                invocationArguments.Add("this");
            }

            for (var index = 0; index < function.Parameters.Count; index++)
            {
                invocationArguments.Add(BuildGenericInvocationArgument(function.Parameters[index], concreteParameters[index]));
            }

            var importMethodName = GetGenericImportMethodName(function, instantiation);
            writer.WriteLine($"if ({BuildGenericInstantiationCondition(record, instantiation)})");
            writer.WriteLine("{");

            if (concreteReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.WriteLine($"    {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                writer.WriteLine("    return;");
            }
            else if (function.IsConstructor)
            {
                writer.WriteLine($"    return {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
            }
            else if (function.ReturnType.IsGenericParameter)
            {
                writer.WriteLine($"    var __result = {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                writer.WriteLine($"    return ({GetManagedPublicReturnTypeName(function.ReturnType)})(object)__result!;");
            }
            else if (concreteReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.WriteLine($"    var __result = {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                var wrapExpression = BuildManagedHandleWrap(compilation, concreteReturnType, "__result", function.ReturnsOwnedHandle);
                if (function.ReturnType.RecordId == record.SymbolId && function.ReturnType.ContainsGenericParameters)
                {
                    writer.WriteLine($"    return ({GetManagedPublicReturnTypeName(function.ReturnType)})(object){wrapExpression}!;");
                }
                else
                {
                    writer.WriteLine($"    return {wrapExpression};");
                }
            }
            else
            {
                writer.WriteLine($"    return {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
            }

            writer.WriteLine("}");
        }

        return writer.ToString();
    }

    private static string RenderGenericDeleteHelper(BindingCompilation compilation, RecordBinding record, IReadOnlyList<RecordGenericInstantiationBinding> instantiations)
    {
        var writer = new CodeWriter();
        writer.Write(
            """
            private static void DeleteHandle(nint handle)
            {
            """);

        foreach (var instantiation in instantiations)
        {
            writer.WriteLine($"if ({BuildGenericInstantiationCondition(record, instantiation)})");
            writer.WriteLine("{");
            writer.WriteLine($"    {GetGenericImportAccessor(compilation)}.{GetGenericDeleteImportMethodName(record, instantiation)}(handle);");
            writer.WriteLine("    return;");
            writer.WriteLine("}");
        }

        writer.Write($$"""
            throw new NotSupportedException("SharpCpp does not support the requested generic argument combination for delete on '{{record.ManagedName}}'.");
            }

            protected override bool ReleaseNativeHandle(nint handle)
            {
            if (OwnsNativeHandle)
            {
                DeleteHandle(handle);
            }
            return true;
            }
            """);
        return writer.ToString();
    }

    private static string RenderGenericConstructorImportDeclaration(BindingCompilation compilation, RecordBinding record, FunctionBinding function, RecordGenericInstantiationBinding instantiation)
    {
        var parameterList = string.Join(", ", function.Parameters.Select((parameter, index) => RenderConcreteManagedParameter(parameter, SubstituteGenericType(record, parameter.Type, instantiation))));
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetGenericEntryPointName(function, instantiation)}}")]
            internal static partial nint {{GetGenericImportMethodName(function, instantiation)}}({{parameterList}});
            """;
    }

    private static string RenderGenericInstanceImportDeclaration(BindingCompilation compilation, RecordBinding record, FunctionBinding function, RecordGenericInstantiationBinding instantiation)
    {
        var parameterList = string.Join(", ", new[] { "global::SharpCpp.Support.NativeObject self" }.Concat(function.Parameters.Select((parameter, index) => RenderConcreteManagedParameter(parameter, SubstituteGenericType(record, parameter.Type, instantiation)))));
        var concreteReturnType = SubstituteGenericType(record, function.ReturnType, instantiation);
        var returnTypeName = GetManagedImportTypeName(concreteReturnType);
        var returnAttribute = GetManagedReturnAttribute(concreteReturnType);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} {GetGenericImportMethodName(function, instantiation)}({parameterList});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} {GetGenericImportMethodName(function, instantiation)}({parameterList});";

        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetGenericEntryPointName(function, instantiation)}}")]
            {{importMethodLine}}
            """;
    }

    private static string RenderGenericStaticImportDeclaration(BindingCompilation compilation, RecordBinding record, FunctionBinding function, RecordGenericInstantiationBinding instantiation)
    {
        var parameterList = string.Join(", ", function.Parameters.Select((parameter, index) => RenderConcreteManagedParameter(parameter, SubstituteGenericType(record, parameter.Type, instantiation))));
        var concreteReturnType = SubstituteGenericType(record, function.ReturnType, instantiation);
        var returnTypeName = GetManagedImportTypeName(concreteReturnType);
        var returnAttribute = GetManagedReturnAttribute(concreteReturnType);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} {GetGenericImportMethodName(function, instantiation)}({parameterList});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} {GetGenericImportMethodName(function, instantiation)}({parameterList});";

        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetGenericEntryPointName(function, instantiation)}}")]
            {{importMethodLine}}
            """;
    }

    private static string RenderGenericDeleteImportDeclaration(BindingCompilation compilation, RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetGenericDeleteEntryPointName(record, instantiation)}}")]
            internal static partial void {{GetGenericDeleteImportMethodName(record, instantiation)}}(nint self);
            """;
    }

    private static string RenderConcreteManagedParameter(ParameterBinding parameter, BindingType concreteType)
    {
        var modifier = parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => "in ",
            BindingParameterDirection.Ref => "ref ",
            BindingParameterDirection.Out => "out ",
            _ => string.Empty
        };
        var attribute = GetManagedParameterAttribute(concreteType);
        return $"{attribute}{modifier}{GetManagedImportTypeName(concreteType)} {parameter.ManagedName}";
    }

    private static BindingType SubstituteGenericType(RecordBinding record, BindingType type, RecordGenericInstantiationBinding instantiation)
    {
        if (type.IsGenericParameter)
        {
            var index = record.GenericParameters.ToList().FindIndex(parameter => string.Equals(parameter.Name, type.GenericParameterName, StringComparison.Ordinal));
            return index >= 0 && index < instantiation.TypeArguments.Count
                ? instantiation.TypeArguments[index]
                : type;
        }

        if (type.GenericArguments.Count == 0)
        {
            return type;
        }

        var substitutedArguments = type.GenericArguments.Select(argument => SubstituteGenericType(record, argument, instantiation)).ToArray();
        if (type.Kind == BindingTypeKind.Handle)
        {
            var resolvedRecord = record.SymbolId == type.RecordId ? record : null;
            var cppTypeName = resolvedRecord is not null
                ? $"{resolvedRecord.FormatCppTypeName(substitutedArguments)}*"
                : type.CppTypeName;
            return BindingType.CreateHandle(cppTypeName, resolvedRecord?.ManagedTypeFormat ?? resolvedRecord?.ManagedName ?? type.Marshaller.PublicTypeName, type.RecordId, type.MarshallerTypeName, substitutedArguments);
        }

        if (type.Kind == BindingTypeKind.Enum)
        {
            return BindingType.CreateEnum(type.CppTypeName, type.Marshaller.PublicTypeName, substitutedArguments);
        }

        return type;
    }

    private static string BuildGenericInstantiationCondition(RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        if (record.GenericParameters.Count == 0 || instantiation.TypeArguments.Count == 0)
        {
            return "true";
        }

        return string.Join(
            " && ",
            record.GenericParameters.Select((parameter, index) => $"typeof({parameter.Name}) == typeof({GetManagedWrapperTypeName(instantiation.TypeArguments[index])})"));
    }

    private static string BuildGenericInvocationArgument(ParameterBinding parameter, BindingType concreteType)
    {
        if (!parameter.Type.IsGenericParameter)
        {
            return RenderManagedInvocationArgument(parameter);
        }

        return $"({GetManagedWrapperTypeName(concreteType)})(object){parameter.ManagedName}!";
    }

    private static string GetGenericConstructorFactoryName(FunctionBinding function)
    {
        return $"__CreateHandle_{GetManagedImportMethodName(function)}";
    }

    private static string GetGenericImportMethodName(FunctionBinding function, RecordGenericInstantiationBinding instantiation)
    {
        var ownerPrefix = function.FindOwningRecord() is { } ownerRecord
            ? $"{SanitizeManagedToken(ownerRecord.ManagedName)}_"
            : string.Empty;
        return $"{ownerPrefix}{GetManagedImportMethodName(function)}{GetGenericInstantiationSuffix(instantiation)}";
    }

    private static string GetGenericDeleteImportMethodName(RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        return $"Delete_{SanitizeManagedToken(record.ManagedName)}{GetGenericInstantiationSuffix(instantiation)}";
    }

    private static string GetGenericEntryPointName(FunctionBinding function, RecordGenericInstantiationBinding instantiation)
    {
        return $"{function.EntryPointName}{GetGenericInstantiationSuffix(instantiation)}";
    }

    private static string GetGenericDeleteEntryPointName(RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        return $"sharpcpp_e2e_{record.NativeName}_delete{GetGenericInstantiationSuffix(instantiation)}";
    }

    private static string GetGenericInstantiationSuffix(RecordGenericInstantiationBinding instantiation)
    {
        if (instantiation.TypeArguments.Count == 0)
        {
            return string.Empty;
        }

        return "__" + string.Join("__", instantiation.TypeArguments.Select(argument => SanitizeManagedToken(argument.CppTypeName)));
    }

    private static string RenderNativeGenericConstructor(RecordBinding record, FunctionBinding constructor, RecordGenericInstantiationBinding instantiation)
    {
        var concreteParameters = constructor.Parameters.Select(parameter => SubstituteGenericType(record, parameter.Type, instantiation)).ToArray();
        var parameterList = string.Join(", ", constructor.Parameters.Select((parameter, index) => $"arg_t<{concreteParameters[index].CppTypeName}> {parameter.NativeName}"));
        var abiArguments = string.Join(", ", constructor.Parameters.Select(static parameter => parameter.NativeName));
        var constructorArgTypes = string.Join(", ", concreteParameters.Select(static parameter => parameter.CppTypeName));
        var concreteRecordType = record.FormatCppTypeName(instantiation.TypeArguments);

        return $$"""
            SHARPCPP_EXPORT ret_t<{{concreteRecordType}}*> {{GetGenericEntryPointName(constructor, instantiation)}}({{parameterList}})
            {
            return MarshallerInvoker::construct<{{concreteRecordType}}{{(constructorArgTypes.Length > 0 ? $", {constructorArgTypes}" : string.Empty)}}>({{abiArguments}});
            }
            """;
    }

    private static string RenderNativeGenericInstanceMethod(RecordBinding record, FunctionBinding method, RecordGenericInstantiationBinding instantiation)
    {
        var concreteRecordType = record.FormatCppTypeName(instantiation.TypeArguments);
        var concreteParameters = method.Parameters.Select(parameter => SubstituteGenericType(record, parameter.Type, instantiation)).ToArray();
        var concreteReturnType = SubstituteGenericType(record, method.ReturnType, instantiation);
        var parameterList = string.Join(", ", new[] { $"arg_t<{concreteRecordType}*> self" }.Concat(method.Parameters.Select((parameter, index) => $"arg_t<{concreteParameters[index].CppTypeName}> {parameter.NativeName}")));
        var abiArguments = string.Join(", ", method.Parameters.Select(static parameter => parameter.NativeName));
        var memberSignature = $"{concreteReturnType.CppTypeName}({concreteRecordType}::*)({string.Join(", ", concreteParameters.Select(static parameter => parameter.CppTypeName))}){(method.IsConstMethod ? " const" : string.Empty)}";

        return $$"""
            SHARPCPP_EXPORT ret_t<{{concreteReturnType.CppTypeName}}> {{GetGenericEntryPointName(method, instantiation)}}({{parameterList}})
            {
            using FT = {{memberSignature}};
            {{(concreteReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                ? $"MarshallerInvoker::invoke<FT>(&{concreteRecordType}::{method.NativeName}, self{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});"
                : $"return MarshallerInvoker::invoke<FT>(&{concreteRecordType}::{method.NativeName}, self{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});")}}
            }
            """;
    }

    private static string RenderNativeGenericStaticMethod(RecordBinding record, FunctionBinding method, RecordGenericInstantiationBinding instantiation)
    {
        var concreteRecordType = record.FormatCppTypeName(instantiation.TypeArguments);
        var concreteParameters = method.Parameters.Select(parameter => SubstituteGenericType(record, parameter.Type, instantiation)).ToArray();
        var concreteReturnType = SubstituteGenericType(record, method.ReturnType, instantiation);
        var parameterList = string.Join(", ", method.Parameters.Select((parameter, index) => $"arg_t<{concreteParameters[index].CppTypeName}> {parameter.NativeName}"));
        var abiArguments = string.Join(", ", method.Parameters.Select(static parameter => parameter.NativeName));
        var signature = $"{concreteReturnType.CppTypeName}({string.Join(", ", concreteParameters.Select(static parameter => parameter.CppTypeName))})";

        return $$"""
            SHARPCPP_EXPORT ret_t<{{concreteReturnType.CppTypeName}}> {{GetGenericEntryPointName(method, instantiation)}}({{parameterList}})
            {
            using FT = {{signature}};
            {{(concreteReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                ? $"MarshallerInvoker::invoke_as<FT>(&{concreteRecordType}::{method.NativeName}{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});"
                : $"return MarshallerInvoker::invoke_as<FT>(&{concreteRecordType}::{method.NativeName}{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});")}}
            }
            """;
    }

    private static string RenderNativeGenericDelete(RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        var concreteRecordType = record.FormatCppTypeName(instantiation.TypeArguments);
        return $$"""
            SHARPCPP_EXPORT ret_t<void> {{GetGenericDeleteEntryPointName(record, instantiation)}}(arg_t<{{concreteRecordType}}*> self)
            {
            MarshallerInvoker::destroy<{{concreteRecordType}}>(self);
            }
            """;
    }
}
