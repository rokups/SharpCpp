using SharpCpp.Model.Ast;
using SharpCpp.Model.Utilities;

namespace SharpCpp.Rendering;

public static partial class BindingOutputRenderer
{
    private static string RenderManagedGenericFreeFunction(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var methodTypeParams = string.Join(", ", function.GenericParameters.Select(static param => param.Name));
        var returnTypeName = GetGenericManagedReturnType(function.ReturnType);
        var parameterList = string.Join(", ", function.Parameters.Select(p => RenderManagedPublicParameter(p, compilation)));
        var instantiations = function.SupportedGenericInstantiations.OrderBy(
            static instantiation => string.Join("|", instantiation.TypeArguments.Select(static argument => argument.CppTypeName)),
            StringComparer.Ordinal).ToArray();

        writer.Write($$"""
            public static {{returnTypeName}} {{function.ManagedName}}<{{methodTypeParams}}>({{parameterList}})
            {
            """);

        var methodParams = (IReadOnlyList<BindingGenericParameter>)function.GenericParameters.ToList();
        foreach (var instantiation in instantiations)
        {
            var concreteParameters = function.Parameters.Select(parameter => SubstituteFunctionGenericType(parameter.Type, methodParams, instantiation)).ToArray();
            var concreteReturnType = SubstituteFunctionGenericType(function.ReturnType, methodParams, instantiation);
            var condition = BuildFunctionInstantiationCondition(compilation, function, instantiation);
            var importMethodName = GetGenericImportMethodName(function, instantiation);
            var invocationArguments = new List<string>();

            for (var index = 0; index < function.Parameters.Count; index++)
            {
                invocationArguments.Add(BuildGenericInvocationArgument(compilation, function.Parameters[index], concreteParameters[index]));
            }

            writer.WriteLine($"    if ({condition})");
            writer.WriteLine("    {");
            var keepAliveStatements = BuildGenericKeepAliveStatements(function.Parameters, concreteParameters);

            if (concreteReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.WriteLine($"        {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                foreach (var keepAlive in keepAliveStatements)
                {
                    writer.WriteLine($"        {keepAlive}");
                }
                writer.WriteLine("        return;");
            }
            else if (function.ReturnType.IsGenericParameter)
            {
                writer.WriteLine($"        var __result = {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                foreach (var keepAlive in keepAliveStatements)
                {
                    writer.WriteLine($"        {keepAlive}");
                }
                if (concreteReturnType.Kind == BindingTypeKind.Handle)
                {
                    writer.WriteLine($"        return ({returnTypeName})(object){BuildManagedHandleWrap(compilation, concreteReturnType, "__result", function.ReturnsOwnedHandle)}!;");
                }
                else
                {
                    writer.WriteLine($"        return ({returnTypeName})(object)__result!;");
                }
            }
            else if (concreteReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.WriteLine($"        var __result = {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                foreach (var keepAlive in keepAliveStatements)
                {
                    writer.WriteLine($"        {keepAlive}");
                }
                writer.WriteLine($"        return {BuildManagedHandleWrap(compilation, concreteReturnType, "__result", function.ReturnsOwnedHandle)};");
            }
            else
            {
                writer.WriteLine($"        var __result = {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                foreach (var keepAlive in keepAliveStatements)
                {
                    writer.WriteLine($"        {keepAlive}");
                }
                writer.WriteLine("        return __result;");
            }

            writer.WriteLine("    }");
        }

        writer.Write($$"""
            throw new NotSupportedException("SharpCpp does not support the requested generic argument combination for '{{function.ManagedName}}'.");
            }
            """);
        return writer.ToString();
    }

    private static IEnumerable<string> CollectGenericFreeFunctionImportDeclarations(BindingCompilation compilation, FunctionBinding function)
    {
        foreach (var instantiation in function.SupportedGenericInstantiations.OrderBy(
                     static item => string.Join("|", item.TypeArguments.Select(static argument => argument.CppTypeName)),
                     StringComparer.Ordinal))
        {
            yield return RenderGenericFreeFunctionImportDeclaration(compilation, function, instantiation);
        }
    }

    private static string RenderGenericRecord(BindingCompilation compilation, RecordBinding record)
    {
        ValidateGenericRecordSlice(record);

        var writer = new CodeWriter();
        var constructors = record.Members.OfType<FunctionBinding>().Where(static function => function.IsConstructor && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.Parameters.Count).ToArray();
        var instanceMethods = record.Members.OfType<FunctionBinding>().Where(static function => !function.IsConstructor && !function.IsStatic && function.OperatorToken is null && !function.IsConversionOperator && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
        var genericInstanceMethods = instanceMethods.Where(static function => function.IsGeneric).ToArray();
        var concreteInstanceMethods = instanceMethods.Where(static function => !function.IsGeneric).ToArray();
        var staticMethods = record.Members.OfType<FunctionBinding>().Where(static function => function.IsStatic && function.OperatorToken is null && !function.IsConversionOperator && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
        var instantiations = record.SupportedGenericInstantiations.OrderBy(static instantiation => string.Join("|", instantiation.TypeArguments.Select(static argument => argument.CppTypeName)), StringComparer.Ordinal).ToArray();
        var baseTypeName = GetGenericManagedBaseTypeName(record);
        var genericConstraints = GetGenericRecordConstraints(record);
        var canEmitClone = CanEmitClone(record, isEffectivelyAbstract: false, canEmitDeleteThunk: true);

        WriteDocumentationComment(writer, record);
        writer.Write($$"""
            public partial class {{GetGenericRecordDeclarationName(record)}} : {{baseTypeName}}{{(canEmitClone ? ", global::System.ICloneable" : string.Empty)}}{{genericConstraints}}
            {
            internal static readonly {{GetInstanceCacheFieldTypeName(record)}} s_instanceCache = {{GetInstanceCacheFactoryExpression(record)}};

            protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => s_instanceCache;

            internal {{record.ManagedName}}(nint handle, bool ownsHandle = false)
                : base(handle, ownsHandle)
            {
                if (!IsInvalid)
                {
                    PrepareForOwnedRegistration();
                    s_instanceCache.Register(this);
                }
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

        if (canEmitClone)
        {
            writer.WriteLine();
            writer.Write(RenderGenericManagedCloneMethod(compilation, record, instantiations));
        }

        if (record.EmitDefaultNativeReleaseOverride)
        {
            writer.WriteLine();
            writer.Write(RenderGenericDeleteHelper(compilation, record, instantiations));
        }

        writer.WriteLine();
        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static IEnumerable<string> CollectGenericImportDeclarations(BindingCompilation compilation, RecordBinding record)
    {
        var constructors = record.Members.OfType<FunctionBinding>().Where(static function => function.IsConstructor && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.Parameters.Count).ToArray();
        var instanceMethods = record.Members.OfType<FunctionBinding>().Where(static function => !function.IsConstructor && !function.IsStatic && function.OperatorToken is null && !function.IsConversionOperator && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
        var concreteInstanceMethods = instanceMethods.Where(static function => !function.IsGeneric).ToArray();
        var genericInstanceMethods = instanceMethods.Where(static function => function.IsGeneric).ToArray();
        var staticMethods = record.Members.OfType<FunctionBinding>().Where(static function => function.IsStatic && function.OperatorToken is null && !function.IsConversionOperator && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
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

        if (CanEmitClone(record, isEffectivelyAbstract: false, canEmitDeleteThunk: true))
        {
            foreach (var instantiation in instantiations)
            {
                yield return RenderGenericCloneImportDeclaration(compilation, record, instantiation);
            }
        }

        foreach (var method in genericInstanceMethods)
        {
            var methodInstantiations = method.SupportedGenericInstantiations.ToArray();
            if (methodInstantiations.Length == 0)
            {
                continue;
            }

            var methodParams = (IReadOnlyList<BindingGenericParameter>)method.GenericParameters.ToList();
            foreach (var recordInstantiation in instantiations)
            {
                foreach (var methodInstantiation in methodInstantiations)
                {
                    yield return RenderGenericMethodImportDeclaration(compilation, record, method, methodParams, recordInstantiation, methodInstantiation);
                }
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
            if (instantiation.TypeArguments.Any(argument => argument.Kind is not (BindingTypeKind.Primitive or BindingTypeKind.Enum or BindingTypeKind.Handle)))
            {
                throw new NotSupportedException($"Generic record '{record.ManagedName}' currently supports only primitive/enum/handle concrete type arguments.");
            }
        }
    }

    private static string GetGenericParameterList(RecordBinding record)
    {
        var visibleParameters = GetManagedVisibleGenericParameters(record);
        return visibleParameters.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", visibleParameters.Select(static parameter => parameter.Name))}>";
    }

    private static string GetGenericRecordDeclarationName(RecordBinding record)
    {
        return $"{record.ManagedName}{GetGenericParameterList(record)}";
    }

    private static string GetGenericManagedBaseTypeName(RecordBinding record)
    {
        if (string.IsNullOrWhiteSpace(record.ManagedBaseTypeName))
        {
            return "global::SharpCpp.Support.NativeObject";
        }

        if (!record.ManagedBaseTypeName.Contains('{', StringComparison.Ordinal))
        {
            return record.ManagedBaseTypeName;
        }

        var typeArguments = record.GenericParameters.Select(static parameter => parameter.Name).Cast<object>().ToArray();
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, record.ManagedBaseTypeName, typeArguments);
    }

    private static string GetGenericRecordConstraints(RecordBinding record)
    {
        if (string.IsNullOrWhiteSpace(record.ManagedBaseTypeConstraints) || record.GenericParameters.Count == 0)
        {
            return string.Empty;
        }

        var typeArguments = record.GenericParameters.Select(static parameter => parameter.Name).Cast<object>().ToArray();
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, record.ManagedBaseTypeConstraints, typeArguments);
    }

    private static string GetGenericImportAccessor(BindingCompilation compilation)
    {
        return $"global::{compilation.ManagedNamespace}.{compilation.ManagedClassName}.__PInvoke";
    }

    private static string RenderGenericManagedConstructor(BindingCompilation compilation, RecordBinding record, FunctionBinding function, IReadOnlyList<RecordGenericInstantiationBinding> instantiations)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var parameterList = string.Join(", ", function.Parameters.Select(p => RenderManagedPublicParameter(p, compilation)));

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
        var parameterList = string.Join(", ", function.Parameters.Select(p => RenderManagedPublicParameter(p, compilation)));
        var returnTypeName = GetManagedPublicReturnTypeName(function.ReturnType, compilation);
        var visibility = GetManagedVisibilityNormalized(compilation, record, function);

        writer.Write($$"""
            {{visibility}} {{GetManagedInstanceMethodModifier(compilation, record, function)}}{{returnTypeName}} {{function.ManagedName}}({{parameterList}})
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
        var parameterList = string.Join(", ", function.Parameters.Select(p => RenderManagedPublicParameter(p, compilation)));
        var returnTypeName = GetManagedPublicReturnTypeName(function.ReturnType, compilation);

        writer.Write($$"""
            public static {{returnTypeName}} {{function.ManagedName}}({{parameterList}})
            {
            {{RenderGenericManagedDispatchBranches(compilation, record, function, instantiations, includeSelf: false)}}
            throw new NotSupportedException("SharpCpp does not support the requested generic argument combination for '{{function.ManagedName}}'.");
            }
            """);
        return writer.ToString();
    }

    private static string RenderGenericManagedCloneMethod(BindingCompilation compilation, RecordBinding record, IReadOnlyList<RecordGenericInstantiationBinding> instantiations)
    {
        var writer = new CodeWriter();
        var declarationName = GetGenericRecordDeclarationName(record);

        if (!record.HasOwnManagedNonFieldMemberNamed("Clone"))
        {
            var hidesBaseClone = !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) && !record.ManagedBaseTypeName.Contains('.', StringComparison.Ordinal);
            writer.Write($$"""
                public {{(hidesBaseClone ? "new " : string.Empty)}}{{declarationName}} Clone()
                {
                {{RenderGenericManagedCloneDispatchBranches(compilation, record, instantiations)}}
                throw new NotSupportedException("SharpCpp does not support the requested generic argument combination for clone on '{{record.ManagedName}}'.");
                }

                object global::System.ICloneable.Clone()
                {
                return Clone();
                }
                """);
        }
        else
        {
            writer.Write($$"""
                object global::System.ICloneable.Clone()
                {
                {{RenderGenericManagedCloneDispatchBranches(compilation, record, instantiations)}}
                throw new NotSupportedException("SharpCpp does not support the requested generic argument combination for clone on '{{record.ManagedName}}'.");
                }
                """);
        }

        return writer.ToString();
    }

    private static string RenderGenericManagedCloneDispatchBranches(BindingCompilation compilation, RecordBinding record, IReadOnlyList<RecordGenericInstantiationBinding> instantiations)
    {
        var writer = new CodeWriter();
        foreach (var instantiation in instantiations)
        {
            writer.WriteLine($"if ({BuildGenericInstantiationCondition(compilation, record, instantiation)})");
            writer.WriteLine("{");
            writer.WriteLine($"    var __result = {GetGenericImportAccessor(compilation)}.{GetGenericCloneImportMethodName(record, instantiation)}(this);");
            writer.WriteLine($"    return {declarationName(record)}.CreateManagedWrapper(__result, ownsHandle: true);");
            writer.WriteLine("}");
        }

        return writer.ToString();

        static string declarationName(RecordBinding recordBinding) => GetGenericRecordDeclarationName(recordBinding);
    }

    private static string RenderGenericManagedGenericMethod(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var methodTypeParams = string.Join(", ", function.GenericParameters.Select(static param => param.Name));
        var returnTypeName = GetGenericManagedReturnType(function.ReturnType, compilation);
        var parameterList = string.Join(", ", function.Parameters.Select(p => RenderManagedPublicParameter(p, compilation)));
        var methodInstantiations = function.SupportedGenericInstantiations.ToArray();
        var recordInstantiations = record.SupportedGenericInstantiations.OrderBy(
            static instantiation => string.Join("|", instantiation.TypeArguments.Select(static argument => argument.CppTypeName)),
            StringComparer.Ordinal).ToArray();

        if (methodInstantiations.Length == 0 || recordInstantiations.Length == 0)
        {
            writer.Write($$"""
                public {{returnTypeName}} {{function.ManagedName}}<{{methodTypeParams}}>({{parameterList}})
                {
                throw new NotSupportedException("SharpCpp does not yet support generic methods inside generic records for type argument combination <{{methodTypeParams}}>.");
                }
                """);
            return writer.ToString();
        }

        writer.Write($$"""
            public {{returnTypeName}} {{function.ManagedName}}<{{methodTypeParams}}>({{parameterList}})
            {
            """);

        var methodParams = (IReadOnlyList<BindingGenericParameter>)function.GenericParameters.ToList();
        foreach (var recordInstantiation in recordInstantiations)
        {
            foreach (var methodInstantiation in methodInstantiations)
            {
                var concreteParameters = function.Parameters.Select(parameter =>
                    SubstituteGenericType(record, parameter.Type, recordInstantiation, methodParams, methodInstantiation)).ToArray();
                var concreteReturnType = SubstituteGenericType(record, function.ReturnType, recordInstantiation, methodParams, methodInstantiation);
                var concreteRecordType = record.FormatCppTypeName(recordInstantiation.TypeArguments);

                var condition = BuildCombinedGenericInstantiationCondition(compilation, record, recordInstantiation, function, methodInstantiation);
                var importMethodName = GetGenericMethodImportName(function, recordInstantiation, methodInstantiation);

                var invocationArguments = new List<string> { $"(global::SharpCpp.Support.NativeObject)this" };
                for (var index = 0; index < function.Parameters.Count; index++)
                {
                    invocationArguments.Add(BuildGenericInvocationArgument(compilation, function.Parameters[index], concreteParameters[index]));
                }

                writer.WriteLine($"    if ({condition})");
                writer.WriteLine("    {");
                var keepAliveStatements = BuildGenericKeepAliveStatements(function.Parameters, concreteParameters);

                if (concreteReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
                {
                    writer.WriteLine($"        {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                    foreach (var keepAlive in keepAliveStatements)
                    {
                        writer.WriteLine($"        {keepAlive}");
                    }
                    writer.WriteLine("        return;");
                }
                else if (function.ReturnType.IsGenericParameter)
                {
                    writer.WriteLine($"        var __result = {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                    foreach (var keepAlive in keepAliveStatements)
                    {
                        writer.WriteLine($"        {keepAlive}");
                    }
                    if (concreteReturnType.Kind == BindingTypeKind.Handle)
                    {
                        writer.WriteLine($"        return ({returnTypeName})(object){BuildManagedHandleWrap(compilation, concreteReturnType, "__result", function.ReturnsOwnedHandle)}!;");
                    }
                    else
                    {
                        writer.WriteLine($"        return ({returnTypeName})(object)__result!;");
                    }
                }
                else if (concreteReturnType.Kind == BindingTypeKind.Handle)
                {
                    writer.WriteLine($"        var __result = {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                    foreach (var keepAlive in keepAliveStatements)
                    {
                        writer.WriteLine($"        {keepAlive}");
                    }
                    writer.WriteLine($"        return {BuildManagedHandleWrap(compilation, concreteReturnType, "__result", function.ReturnsOwnedHandle)};");
                }
                else
                {
                    writer.WriteLine($"        var __result = {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                    foreach (var keepAlive in keepAliveStatements)
                    {
                        writer.WriteLine($"        {keepAlive}");
                    }
                    writer.WriteLine("        return __result;");
                }

                writer.WriteLine("    }");
            }
        }

        writer.Write($$"""
            throw new NotSupportedException("SharpCpp does not support the requested generic argument combination for '{{function.ManagedName}}'.");
            }
            """);
        return writer.ToString();
    }

    private static string BuildCombinedGenericInstantiationCondition(
        BindingCompilation compilation,
        RecordBinding record,
        RecordGenericInstantiationBinding recordInstantiation,
        FunctionBinding function,
        RecordGenericInstantiationBinding methodInstantiation)
    {
        var parts = new List<string>();

        foreach (var index in GetManagedVisibleGenericParameterIndices(record))
        {
            parts.Add($"typeof({record.GenericParameters[index].Name}) == typeof({GetManagedWrapperTypeName(recordInstantiation.TypeArguments[index], compilation)})");
        }

        for (var index = 0; index < function.GenericParameters.Count; index++)
        {
            parts.Add($"typeof({function.GenericParameters[index].Name}) == typeof({GetManagedWrapperTypeName(methodInstantiation.TypeArguments[index], compilation)})");
        }

        return string.Join(" && ", parts);
    }

    private static string GetGenericMethodEntryPointName(FunctionBinding function, RecordGenericInstantiationBinding recordInstantiation, RecordGenericInstantiationBinding methodInstantiation)
    {
        return $"{function.EntryPointName}{GetGenericInstantiationSuffix(recordInstantiation)}{GetGenericInstantiationSuffix(methodInstantiation)}";
    }

    private static string GetGenericMethodImportName(FunctionBinding function, RecordGenericInstantiationBinding recordInstantiation, RecordGenericInstantiationBinding methodInstantiation)
    {
        return $"{GetManagedImportMethodName(function)}{GetGenericInstantiationSuffix(recordInstantiation)}{GetGenericInstantiationSuffix(methodInstantiation)}";
    }

    private static string GetGenericManagedReturnType(BindingType type, BindingCompilation? compilation = null)
    {
        if (type.IsGenericParameter)
        {
            return type.GenericParameterName ?? "object";
        }

        return GetManagedPublicReturnTypeName(type, compilation);
    }

    private static string BuildFunctionInstantiationCondition(BindingCompilation compilation, FunctionBinding function, RecordGenericInstantiationBinding instantiation)
    {
        if (function.GenericParameters.Count == 0 || instantiation.TypeArguments.Count == 0)
        {
            return "true";
        }

        return string.Join(
            " && ",
            function.GenericParameters.Select((parameter, index) => $"typeof({parameter.Name}) == typeof({GetManagedWrapperTypeName(instantiation.TypeArguments[index], compilation)})"));
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
                invocationArguments.Add(BuildGenericInvocationArgument(compilation, function.Parameters[index], concreteParameters[index]));
            }

            var importMethodName = GetGenericImportMethodName(function, instantiation);
            writer.WriteLine($"if ({BuildGenericInstantiationCondition(compilation, record, instantiation)})");
            writer.WriteLine("{");
            var keepAliveStatements = BuildGenericKeepAliveStatements(function.Parameters, concreteParameters);

            if (concreteReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.WriteLine($"    {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                foreach (var keepAlive in keepAliveStatements)
                {
                    writer.WriteLine($"    {keepAlive}");
                }
                writer.WriteLine("    return;");
            }
            else if (function.IsConstructor)
            {
                writer.WriteLine($"    return {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
            }
            else if (function.ReturnType.IsGenericParameter)
            {
                writer.WriteLine($"    var __result = {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                foreach (var keepAlive in keepAliveStatements)
                {
                    writer.WriteLine($"    {keepAlive}");
                }
                if (concreteReturnType.Kind == BindingTypeKind.Handle)
                {
                    writer.WriteLine($"    return ({GetManagedPublicReturnTypeName(function.ReturnType, compilation)})(object){BuildManagedHandleWrap(compilation, concreteReturnType, "__result", function.ReturnsOwnedHandle)}!;");
                }
                else
                {
                    writer.WriteLine($"    return ({GetManagedPublicReturnTypeName(function.ReturnType, compilation)})(object)__result!;");
                }
            }
            else if (concreteReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.WriteLine($"    var __result = {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                var wrapExpression = BuildManagedHandleWrap(compilation, concreteReturnType, "__result", function.ReturnsOwnedHandle);
                foreach (var keepAlive in keepAliveStatements)
                {
                    writer.WriteLine($"    {keepAlive}");
                }
                if (function.ReturnType.RecordId == record.SymbolId && function.ReturnType.ContainsGenericParameters)
                {
                    writer.WriteLine($"    return ({GetManagedPublicReturnTypeName(function.ReturnType, compilation)})(object){wrapExpression}!;");
                }
                else
                {
                    writer.WriteLine($"    return {wrapExpression};");
                }
            }
            else
            {
                writer.WriteLine($"    var __result = {GetGenericImportAccessor(compilation)}.{importMethodName}({string.Join(", ", invocationArguments)});");
                foreach (var keepAlive in keepAliveStatements)
                {
                    writer.WriteLine($"    {keepAlive}");
                }
                writer.WriteLine("    return __result;");
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
            writer.WriteLine($"if ({BuildGenericInstantiationCondition(compilation, record, instantiation)})");
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
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetGenericEntryPointName(function, instantiation)}}", StringMarshalling = StringMarshalling.Utf8)]
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
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetGenericEntryPointName(function, instantiation)}}", StringMarshalling = StringMarshalling.Utf8)]
            {{importMethodLine}}
            """;
    }

    private static string RenderGenericMethodImportDeclaration(
        BindingCompilation compilation,
        RecordBinding record,
        FunctionBinding function,
        IReadOnlyList<BindingGenericParameter> methodParams,
        RecordGenericInstantiationBinding recordInstantiation,
        RecordGenericInstantiationBinding methodInstantiation)
    {
        var concreteParameters = function.Parameters.Select((parameter, index) =>
            RenderConcreteManagedParameter(parameter,
                SubstituteGenericType(record, parameter.Type, recordInstantiation, methodParams, methodInstantiation))).ToArray();
        var concreteReturnType = SubstituteGenericType(record, function.ReturnType, recordInstantiation, methodParams, methodInstantiation);
        var parameterList = string.Join(", ", new[] { "global::SharpCpp.Support.NativeObject self" }.Concat(concreteParameters));
        var returnTypeName = GetManagedImportTypeName(concreteReturnType);
        var returnAttribute = GetManagedReturnAttribute(concreteReturnType);
        var importMethodName = GetGenericMethodImportName(function, recordInstantiation, methodInstantiation);
        var entryPointName = GetGenericMethodEntryPointName(function, recordInstantiation, methodInstantiation);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} {importMethodName}({parameterList});";

        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{entryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
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
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetGenericEntryPointName(function, instantiation)}}", StringMarshalling = StringMarshalling.Utf8)]
            {{importMethodLine}}
            """;
    }

    private static string RenderGenericFreeFunctionImportDeclaration(BindingCompilation compilation, FunctionBinding function, RecordGenericInstantiationBinding instantiation)
    {
        var methodParams = (IReadOnlyList<BindingGenericParameter>)function.GenericParameters.ToList();
        var parameterList = string.Join(", ", function.Parameters.Select((parameter, index) => RenderConcreteManagedParameter(parameter, SubstituteFunctionGenericType(parameter.Type, methodParams, instantiation))));
        var concreteReturnType = SubstituteFunctionGenericType(function.ReturnType, methodParams, instantiation);
        var returnTypeName = GetManagedImportTypeName(concreteReturnType);
        var returnAttribute = GetManagedReturnAttribute(concreteReturnType);
        var importMethodName = GetGenericImportMethodName(function, instantiation);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} {importMethodName}({parameterList});";

        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetGenericEntryPointName(function, instantiation)}}", StringMarshalling = StringMarshalling.Utf8)]
            {{importMethodLine}}
            """;
    }

    private static string RenderGenericDeleteImportDeclaration(BindingCompilation compilation, RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetGenericDeleteEntryPointName(record, instantiation)}}", StringMarshalling = StringMarshalling.Utf8)]
            internal static partial void {{GetGenericDeleteImportMethodName(record, instantiation)}}(nint self);
            """;
    }

    private static string RenderGenericCloneImportDeclaration(BindingCompilation compilation, RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetGenericCloneEntryPointName(record, instantiation)}}", StringMarshalling = StringMarshalling.Utf8)]
            internal static partial nint {{GetGenericCloneImportMethodName(record, instantiation)}}(global::SharpCpp.Support.NativeObject self);
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

    private static BindingType SubstituteGenericType(RecordBinding record, BindingType type, RecordGenericInstantiationBinding instantiation, IReadOnlyList<BindingGenericParameter>? methodParameters = null, RecordGenericInstantiationBinding? methodInstantiation = null)
    {
        if (type.IsGenericParameter)
        {
            if (methodParameters is not null && methodInstantiation is not null)
            {
                var methodIndex = methodParameters.ToList().FindIndex(parameter => string.Equals(parameter.Name, type.GenericParameterName, StringComparison.Ordinal));
                if (methodIndex >= 0 && methodIndex < methodInstantiation.TypeArguments.Count)
                {
                    return methodInstantiation.TypeArguments[methodIndex];
                }
            }

            var index = record.GenericParameters.ToList().FindIndex(parameter => string.Equals(parameter.Name, type.GenericParameterName, StringComparison.Ordinal));
            return index >= 0 && index < instantiation.TypeArguments.Count
                ? instantiation.TypeArguments[index]
                : type;
        }

        if (type.GenericArguments.Count == 0)
        {
            return type;
        }

        var substitutedArguments = type.GenericArguments.Select(argument => SubstituteGenericType(record, argument, instantiation, methodParameters, methodInstantiation)).ToArray();
        if (type.Kind == BindingTypeKind.Handle)
        {
            var resolvedRecord = record.SymbolId == type.RecordId ? record : null;
            var cppTypeName = resolvedRecord is not null
                ? $"{resolvedRecord.FormatCppTypeName(substitutedArguments)}*"
                : type.CppTypeName;
            return BindingType.CreateHandle(cppTypeName, resolvedRecord?.ManagedTypeFormat ?? resolvedRecord?.ManagedName ?? type.ManagedTypeFormatOverride ?? type.Marshaller.PublicTypeName, type.RecordId, type.MarshallerTypeName, substitutedArguments);
        }

        if (type.Kind == BindingTypeKind.Enum)
        {
            return BindingType.CreateEnum(type.CppTypeName, type.Marshaller.PublicTypeName, substitutedArguments);
        }

        return type;
    }

    private static BindingType SubstituteFunctionGenericType(BindingType type, IReadOnlyList<BindingGenericParameter> functionParameters, RecordGenericInstantiationBinding instantiation)
    {
        if (type.IsGenericParameter)
        {
            var index = functionParameters.ToList().FindIndex(parameter => string.Equals(parameter.Name, type.GenericParameterName, StringComparison.Ordinal));
            return index >= 0 && index < instantiation.TypeArguments.Count
                ? instantiation.TypeArguments[index]
                : type;
        }

        if (type.GenericArguments.Count == 0)
        {
            return type;
        }

        var substitutedArguments = type.GenericArguments.Select(argument => SubstituteFunctionGenericType(argument, functionParameters, instantiation)).ToArray();
        if (type.Kind == BindingTypeKind.Handle)
        {
            return BindingType.CreateHandle(type.CppTypeName, type.ManagedTypeFormatOverride ?? type.Marshaller.PublicTypeName, type.RecordId, type.MarshallerTypeName, substitutedArguments);
        }

        if (type.Kind == BindingTypeKind.Enum)
        {
            return BindingType.CreateEnum(type.CppTypeName, type.Marshaller.PublicTypeName, substitutedArguments);
        }

        return type;
    }

    private static string BuildGenericInstantiationCondition(BindingCompilation compilation, RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        var visibleIndices = GetManagedVisibleGenericParameterIndices(record);
        if (visibleIndices.Count == 0 || instantiation.TypeArguments.Count == 0)
        {
            return "true";
        }

        return string.Join(
            " && ",
            visibleIndices.Select(index => $"typeof({record.GenericParameters[index].Name}) == typeof({GetManagedWrapperTypeName(instantiation.TypeArguments[index], compilation)})"));
    }

    private static IReadOnlyList<BindingGenericParameter> GetManagedVisibleGenericParameters(RecordBinding record)
    {
        return GetManagedVisibleGenericParameterIndices(record)
            .Select(index => record.GenericParameters[index])
            .ToArray();
    }

    private static IReadOnlyList<int> GetManagedVisibleGenericParameterIndices(RecordBinding record)
    {
        var usedIndices = new HashSet<int>();
        CollectManagedFormatPlaceholderIndices(record.ManagedTypeFormat, usedIndices);
        CollectManagedFormatPlaceholderIndices(record.ManagedBaseTypeName, usedIndices);
        CollectManagedFormatPlaceholderIndices(record.ManagedBaseTypeConstraints, usedIndices);

        if (usedIndices.Count == 0)
        {
            return Enumerable.Range(0, record.GenericParameters.Count).ToArray();
        }

        return usedIndices
            .Where(index => index >= 0 && index < record.GenericParameters.Count)
            .OrderBy(static index => index)
            .ToArray();
    }

    private static void CollectManagedFormatPlaceholderIndices(string? format, ISet<int> target)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return;
        }

        for (var index = 0; index < format.Length; index++)
        {
            if (format[index] != '{')
            {
                continue;
            }

            var closeIndex = format.IndexOf('}', index + 1);
            if (closeIndex <= index + 1)
            {
                continue;
            }

            if (int.TryParse(format[(index + 1)..closeIndex], out var placeholderIndex))
            {
                target.Add(placeholderIndex);
            }

            index = closeIndex;
        }
    }

    private static string BuildGenericInvocationArgument(BindingCompilation compilation, ParameterBinding parameter, BindingType concreteType)
    {
        if (!parameter.Type.IsGenericParameter)
        {
            return RenderManagedInvocationArgument(parameter);
        }

        if (concreteType.IsPointerType)
        {
            return $"global::SharpCpp.Support.NativeObject.GetHandle({parameter.ManagedName})";
        }

        return $"({GetManagedWrapperTypeName(concreteType, compilation)})(object){parameter.ManagedName}!";
    }

    private static IReadOnlyList<string> BuildGenericKeepAliveStatements(IList<ParameterBinding> parameters, IReadOnlyList<BindingType> concreteTypes)
    {
        var results = new List<string>();
        for (var index = 0; index < parameters.Count; index++)
        {
            if (concreteTypes[index].IsPointerType &&
                parameters[index].Direction is not (BindingParameterDirection.Ref or BindingParameterDirection.Out))
            {
                results.Add($"global::System.GC.KeepAlive({parameters[index].ManagedName});");
            }
        }

        return results;
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

    private static string GetGenericCloneImportMethodName(RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        return $"CloneCopyCtor_{SanitizeManagedToken(record.ManagedName)}{GetGenericInstantiationSuffix(instantiation)}";
    }

    private static string GetGenericEntryPointName(FunctionBinding function, RecordGenericInstantiationBinding instantiation)
    {
        return $"{function.EntryPointName}{GetGenericInstantiationSuffix(instantiation)}";
    }

    private static string GetGenericDeleteEntryPointName(RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        return $"sharpcpp_e2e_{SanitizeNativeEntrypointName(record.NativeName)}_delete{GetGenericInstantiationSuffix(instantiation)}";
    }

    private static string GetGenericCloneEntryPointName(RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        return $"sharpcpp_e2e_{SanitizeNativeEntrypointName(record.NativeName)}_clone{GetGenericInstantiationSuffix(instantiation)}";
    }

    private static string GetGenericInstantiationSuffix(RecordGenericInstantiationBinding instantiation)
    {
        return instantiation.Suffix;
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

    private static string RenderNativeGenericClone(RecordBinding record, RecordGenericInstantiationBinding instantiation)
    {
        var concreteRecordType = record.FormatCppTypeName(instantiation.TypeArguments);
        return $$"""
            SHARPCPP_EXPORT ret_t<{{concreteRecordType}}*> {{GetGenericCloneEntryPointName(record, instantiation)}}(arg_t<{{concreteRecordType}}*> self)
            {
            Marshaller<{{concreteRecordType}}*> self_marshaller;
            auto* instance = static_cast<{{concreteRecordType}}*>(self_marshaller.ConvertToCpp(self));
            return detail::TryClone<{{concreteRecordType}}>(instance, 0);
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

        if (method.TryGetMetadata(MetadataKeys.MarshallerExposedMethod, out string? _))
        {
            return $$"""
                SHARPCPP_EXPORT ret_t<{{concreteReturnType.CppTypeName}}> {{GetGenericEntryPointName(method, instantiation)}}({{parameterList}})
                {
                {{RenderNativeGenericMarshallerExposedInvocation(method, concreteRecordType, concreteReturnType, concreteParameters, abiArguments)}}
                }
                """;
        }

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

    internal static string RenderNativeGenericFreeFunction(FunctionBinding function, RecordGenericInstantiationBinding instantiation)
    {
        var methodParams = (IReadOnlyList<BindingGenericParameter>)function.GenericParameters.ToList();
        var concreteParameters = function.Parameters.Select(parameter => SubstituteFunctionGenericType(parameter.Type, methodParams, instantiation)).ToArray();
        var concreteReturnType = SubstituteFunctionGenericType(function.ReturnType, methodParams, instantiation);
        var parameterList = string.Join(", ", function.Parameters.Select((parameter, index) => $"arg_t<{concreteParameters[index].CppTypeName}> {parameter.NativeName}"));
        var abiArguments = string.Join(", ", function.Parameters.Select(static parameter => parameter.NativeName));
        var signature = $"{concreteReturnType.CppTypeName}({string.Join(", ", concreteParameters.Select(static parameter => parameter.CppTypeName))})";
        var methodCppTypeArgs = string.Join(", ", instantiation.TypeArguments.Select(static argument => argument.CppTypeName));

        return $$"""
            SHARPCPP_EXPORT ret_t<{{concreteReturnType.CppTypeName}}> {{GetGenericEntryPointName(function, instantiation)}}({{parameterList}})
            {
            using FT = {{signature}};
            {{(concreteReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                ? $"MarshallerInvoker::invoke_as<FT>(&{function.NativeName}<{methodCppTypeArgs}>{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});"
                : $"return MarshallerInvoker::invoke_as<FT>(&{function.NativeName}<{methodCppTypeArgs}>{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});")}}
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

    internal static string RenderNativeGenericMethodInstanceMethod(
        RecordBinding record,
        FunctionBinding method,
        RecordGenericInstantiationBinding recordInstantiation,
        RecordGenericInstantiationBinding methodInstantiation)
    {
        var methodParams = (IReadOnlyList<BindingGenericParameter>)method.GenericParameters.ToList();
        var concreteRecordType = record.FormatCppTypeName(recordInstantiation.TypeArguments);
        var concreteParameters = method.Parameters.Select(parameter =>
            SubstituteGenericType(record, parameter.Type, recordInstantiation, methodParams, methodInstantiation)).ToArray();
        var concreteReturnType = SubstituteGenericType(record, method.ReturnType, recordInstantiation, methodParams, methodInstantiation);
        var parameterList = string.Join(", ", new[] { $"arg_t<{concreteRecordType}*> self" }.Concat(method.Parameters.Select((parameter, index) => $"arg_t<{concreteParameters[index].CppTypeName}> {parameter.NativeName}")));
        var abiArguments = string.Join(", ", method.Parameters.Select(static parameter => parameter.NativeName));
        var memberSignature = $"{concreteReturnType.CppTypeName}({concreteRecordType}::*)({string.Join(", ", concreteParameters.Select(static parameter => parameter.CppTypeName))}){(method.IsConstMethod ? " const" : string.Empty)}";
        var methodCppTypeArgs = string.Join(", ", methodInstantiation.TypeArguments.Select(static argument => argument.CppTypeName));

        return $$"""
            SHARPCPP_EXPORT ret_t<{{concreteReturnType.CppTypeName}}> {{GetGenericMethodEntryPointName(method, recordInstantiation, methodInstantiation)}}({{parameterList}})
            {
            using FT = {{memberSignature}};
            {{(concreteReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                ? $"MarshallerInvoker::invoke<FT>(&{concreteRecordType}::{method.NativeName}<{methodCppTypeArgs}>, self{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});"
                : $"return MarshallerInvoker::invoke<FT>(&{concreteRecordType}::{method.NativeName}<{methodCppTypeArgs}>, self{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});")}}
            }
            """;
    }

    private static string RenderNativeGenericMarshallerExposedInvocation(
        FunctionBinding method,
        string concreteRecordType,
        BindingType concreteReturnType,
        IReadOnlyList<BindingType> concreteParameters,
        string abiArguments)
    {
        var signatureParameters = string.Join(", ", new[] { $"{concreteRecordType}*" }.Concat(concreteParameters.Select(static parameter => parameter.CppTypeName)));
        var lambdaParameters = string.Join(", ", new[] { $"{concreteRecordType}* __self" }
            .Concat(concreteParameters.Select((parameter, index) => $"{parameter.CppTypeName} __arg{index}")));
        var invokeArguments = string.Join(", ", new[] { "*__self" }
            .Concat(concreteParameters.Select((_, index) => $"__arg{index}")));
        var callArguments = string.IsNullOrWhiteSpace(abiArguments) ? "self" : $"self, {abiArguments}";
        var returnStatement = concreteReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? $"Marshaller<{concreteRecordType}>::{method.NativeName}({invokeArguments});"
            : $"return Marshaller<{concreteRecordType}>::{method.NativeName}({invokeArguments});";
        var dispatch = concreteReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? $"MarshallerInvoker::invoke_as<FT>(__invoke, {callArguments});"
            : $"return MarshallerInvoker::invoke_as<FT>(__invoke, {callArguments});";

        return $$"""
            using FT = {{concreteReturnType.CppTypeName}}({{signatureParameters}});
            auto __invoke = []({{lambdaParameters}}) -> {{concreteReturnType.CppTypeName}}
            {
            {{returnStatement}}
            };
            {{dispatch}}
            """;
    }
}
