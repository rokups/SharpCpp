using SharpCpp.Model.Ast;
using SharpCpp.Model.Utilities;

using System.Text;

namespace SharpCpp.Rendering;

public static partial class BindingOutputRenderer
{
    public static GeneratedBindingSet Render(BindingCompilation compilation, string fileStem,
        Func<string?, string>? deriveGroupStem = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileStem);

        AssignImportMethodNames(compilation);

        if (deriveGroupStem is null)
        {
            var files = new[]
            {
                new GeneratedBindingFile($"{fileStem}.Bindings.g.cs", RenderManagedBindings(compilation)),
                new GeneratedBindingFile($"{fileStem}.Bindings.Aliases.g.cs", RenderAliasBindings()),
                new GeneratedBindingFile($"{fileStem}.Bindings.g.cpp", RenderNativeBindings(compilation))
            };
            return new GeneratedBindingSet(files);
        }

        var allFiles = new List<GeneratedBindingFile>();

        var groups = compilation.Members.OfType<BindingNode>()
            .Where(m => m is FunctionBinding ||
                        m is RecordBinding record && !record.GetMetadata<bool>(MetadataKeys.SuppressEmission) ||
                        m is EnumBinding @enum && !@enum.GetMetadata<bool>(MetadataKeys.SuppressEmission))
            .GroupBy(m => deriveGroupStem(string.IsNullOrEmpty(m.HeaderIncludePath) ? null : m.HeaderIncludePath), StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToList();

        if (groups.Count == 0)
        {
            allFiles.Add(new GeneratedBindingFile($"{fileStem}.Bindings.g.cs", RenderManagedBindings(compilation)));
            allFiles.Add(new GeneratedBindingFile($"{fileStem}.Bindings.g.cpp", RenderNativeBindings(compilation)));
        }
        else
        {
            foreach (var group in groups)
            {
                var stem = group.Key;
                var memberSet = new HashSet<BindingNode>(group);

                allFiles.Add(new GeneratedBindingFile($"{stem}.g.cs",
                    RenderManagedBindings(compilation, node => memberSet.Contains(node) || (node is RecordBinding r && memberSet.Contains(r)))));
                allFiles.Add(new GeneratedBindingFile($"{stem}.g.cpp",
                    RenderNativeBindings(compilation, node => memberSet.Contains(node) || (node is RecordBinding r && memberSet.Contains(r)))));
            }
        }

        allFiles.Add(new GeneratedBindingFile($"{fileStem}.Bindings.Aliases.g.cs",
            RenderAliasBindings(compilation, deriveGroupStem is not null)));
        return new GeneratedBindingSet(allFiles);
    }

    private static string RenderManagedBindings(BindingCompilation compilation,
        Func<BindingNode, bool>? memberFilter = null, string? groupHeaderPath = null)
    {
        var writer = new CodeWriter();
        var allRecords = compilation.Records.Where(static record => !record.GetMetadata<bool>(MetadataKeys.SuppressEmission)).AsEnumerable();
        var allEnums = compilation.Enums.Where(static @enum => !@enum.GetMetadata<bool>(MetadataKeys.SuppressEmission)).AsEnumerable();
        var allFunctions = compilation.Functions.AsEnumerable();

        if (memberFilter is not null)
        {
            allRecords = allRecords.Where(r => memberFilter(r));
            allEnums = allEnums.Where(e => memberFilter(e));
            allFunctions = allFunctions.Where(f => memberFilter(f));
        }

        var records = allRecords.OrderBy(static record => record.ManagedName, StringComparer.Ordinal).ToArray();
        var enums = allEnums.OrderBy(static item => item.ManagedName, StringComparer.Ordinal).ToArray();
        var functions = allFunctions.OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
        var interfaceRecordIds = compilation.Records
            .SelectMany(record => record.GetSecondaryBaseRecords(compilation))
            .Select(static record => record.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        var interfaceRecords = records
            .Where(record => interfaceRecordIds.Contains(record.SymbolId))
            .GroupBy(static record => record.SymbolId, StringComparer.Ordinal)
            .Select(static group => group.First())
            // Exclude concrete generic instantiations from interface rendering.
            // Each closed instantiation (e.g. IIDFamily<Geometry>, IIDFamily<Material>)
            // has a ManagedName containing angle brackets. Emitting them as separate
            // partial interfaces with different type parameter names causes CS0264.
            .Where(static record => !record.ManagedName.Contains('<'))
            .OrderBy(static record => record.ManagedName, StringComparer.Ordinal)
            .ToArray();
        var polymorphicConcreteRecords = records
            .Where(static record => record.IsPolymorphic && !record.IsEffectivelyAbstract && !record.HasMetadata(MetadataKeys.IgnoreReason))
            .ToArray();
        var genericImportDeclarations = new List<string>();

        writer.Write(
            $$"""
            // <auto-generated />
            #nullable enable
            using System.Runtime.InteropServices;
            using System.Runtime.InteropServices.Marshalling;
            {{(compilation.Records.Any(static record => string.Equals(record.ManagedName, "Object", StringComparison.Ordinal) && !record.IsIgnored()) ? $"using Object = global::{compilation.ManagedNamespace}.Object;" : string.Empty)}}
            using {{compilation.ManagedNamespace}};
            """);

        var recordGroups = records.GroupBy(record => record.ManagedNamespace ?? compilation.ManagedNamespace).ToArray();
        var enumGroups = enums.GroupBy(@enum => @enum.ManagedNamespace ?? compilation.ManagedNamespace).ToArray();
        var allNamespaces = recordGroups.Select(group => group.Key)
            .Concat(enumGroups.Select(group => group.Key))
            .Distinct()
            .OrderBy(ns => ns, StringComparer.Ordinal);

        foreach (var ns in allNamespaces)
        {
            var nsRecords = records.Where(record => (record.ManagedNamespace ?? compilation.ManagedNamespace) == ns).ToArray();
            var nsInterfaces = interfaceRecords.Where(record => (record.ManagedNamespace ?? compilation.ManagedNamespace) == ns).ToArray();
            var nsEnums = enums.Where(@enum => (@enum.ManagedNamespace ?? compilation.ManagedNamespace) == ns).ToArray();

            writer.WriteLine();
            writer.Write(
                $$"""
                namespace {{ns}}
                {
                """);

            foreach (var interfaceRecord in nsInterfaces)
            {
                writer.WriteLine();
                writer.Write(RenderInterface(compilation, interfaceRecord));
            }

            foreach (var record in nsRecords)
            {
                writer.WriteLine();
                if (record.HasMetadata(MetadataKeys.IgnoreReason))
                {
                    writer.Write(RenderIgnoredStub(record));
                }
                else
                {
                    if (record.IsGeneric)
                    {
                        genericImportDeclarations.AddRange(CollectGenericImportDeclarations(compilation, record));
                    }

                    writer.Write(RenderRecord(compilation, record));
                }
            }

            foreach (var @enum in nsEnums)
            {
                writer.WriteLine();
                if (@enum.HasMetadata(MetadataKeys.IgnoreReason))
                {
                    writer.Write(RenderIgnoredStub(@enum));
                }
                else
                {
                    writer.Write(RenderEnum(@enum));
                }
            }

            writer.Write(
                """
                }
                """);
        }

        writer.WriteLine();
        writer.Write(
            $$"""
            namespace {{compilation.ManagedNamespace}}
            {
            """);
        writer.Write(
            $$"""
            public static partial class {{compilation.ManagedClassName}}
            {
            """);

        var freeFunctionDeclarations = new List<string>();
        var lastFreeFunctionWasIgnoredStub = false;

        foreach (var function in functions.Where(static function => function.Parent is not RecordBinding && !function.IsConstructor))
        {
            var isIgnoredStub = function.HasMetadata(MetadataKeys.IgnoreReason);
            writer.WriteLine();
            if (lastFreeFunctionWasIgnoredStub && !isIgnoredStub)
            {
                writer.WriteLine();
            }

            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(function));
            }
            else
            {
                if (function.IsGeneric)
                {
                    writer.Write(RenderManagedGenericFreeFunction(compilation, function));
                    freeFunctionDeclarations.AddRange(CollectGenericFreeFunctionImportDeclarations(compilation, function));
                }
                else
                {
                    writer.Write(RenderManagedWrapperMethod(compilation, function));
                    freeFunctionDeclarations.Add(RenderManagedImportDeclaration(compilation, function));

                    if (HasUnexpressibleDefaultParams(function) || HasUndefaultableParams(function))
                        freeFunctionDeclarations.Add(RenderUnexpressibleDefaultFreeFunctionImport(compilation, function));
                }
            }

            lastFreeFunctionWasIgnoredStub = isIgnoredStub;
        }

        if (lastFreeFunctionWasIgnoredStub)
        {
            writer.WriteLine();
        }

        if (memberFilter is null && polymorphicConcreteRecords.Length > 0)
        {
            writer.WriteLine();
            writer.Write(RenderNativeObjectRegistryInitializer(compilation, polymorphicConcreteRecords));
        }

        if (freeFunctionDeclarations.Count > 0 || genericImportDeclarations.Count > 0)
        {
            writer.WriteLine();
            writer.Write(
                """
                internal static partial class __PInvoke
                {
                """);

            foreach (var declaration in freeFunctionDeclarations)
            {
                writer.WriteLine();
                writer.Write(declaration);
            }

            foreach (var declaration in genericImportDeclarations)
            {
                writer.WriteLine();
                writer.Write(declaration);
            }

            writer.WriteLine();
            writer.Write(
                """
                }
                """);
        }

        writer.WriteLine();
        writer.Write(
            """
            }
            """);

        writer.WriteLine();
        writer.Write(
            """
            }
            """);

        return writer.ToString();
    }


    private static string RenderNativeDirectorClass(BindingCompilation compilation, RecordBinding record)
    {
        var writer = new CodeWriter();
        var directorName = GetNativeDirectorClassName(record);
        var constructors = record.Members.OfType<FunctionBinding>().Where(static function => function.IsConstructor).OrderBy(static function => function.Parameters.Count).ToArray();
        var slots = record.GetEffectiveVirtualMethodSlots(compilation);

        writer.Write($$"""
            class {{directorName}} : public {{record.CppQualifiedName}}
            {
            public:
            """);

        foreach (var (slot, index) in slots.Select((slot, index) => (slot, index)))
        {
            writer.WriteLine($"using {GetNativeDirectorCallbackTypeName(index)} = {BuildNativeDirectorCallbackSignature(slot.Method)};");
        }

        if (slots.Count > 0)
        {
            writer.WriteLine();
        }

        foreach (var constructor in constructors)
        {
            var parameterList = string.Join(", ", constructor.Parameters.Select(parameter => $"{parameter.CppTypeName} {parameter.NativeName}"));
            var arguments = string.Join(", ", constructor.Parameters.Select(static parameter => parameter.NativeName));
            writer.Write($$"""
                {{directorName}}({{parameterList}})
                    : {{record.CppQualifiedName}}({{arguments}})
                {
                }

                """);
        }

        foreach (var method in record.Members.OfType<FunctionBinding>().Where(static function => !function.IsConstructor && function.AccessModifier == BindingAccessModifier.Protected))
        {
            writer.WriteLine();
            writer.Write(RenderNativeProtectedMethodHelper(record, method));
        }

        foreach (var field in record.Members.OfType<FieldBinding>().Where(static field => field.AccessModifier == BindingAccessModifier.Protected))
        {
            writer.WriteLine();
            writer.Write(RenderNativeProtectedFieldHelpers(record, field));
        }

        var callbaseSlots = record.GetEffectiveVirtualMethodSlots(compilation)
            .Where(static slot => !slot.Method.IsPureVirtual)
            .ToArray();
        if (callbaseSlots.Length > 0)
        {
            writer.WriteLine();
            foreach (var slot in callbaseSlots)
            {
                writer.Write(RenderNativeCallbaseHelper(record, slot));
            }
        }

        writer.Write($$"""
            void connect_director({{string.Join(", ", slots.Select((_, index) => $"void* slot{index}"))}})
            {
            """);

        foreach (var (slot, index) in slots.Select((slot, index) => (slot, index)))
        {
            writer.WriteLine($"{GetNativeDirectorCallbackFieldName(index)} = reinterpret_cast<{GetNativeDirectorCallbackTypeName(index)}>(slot{index});");
        }

        writer.WriteLine("}");

        foreach (var (slot, index) in slots.Select((slot, index) => (slot, index)))
        {
            writer.WriteLine();
            writer.Write(RenderNativeDirectorMethod(slot, index));
        }

        writer.WriteLine();
        writer.WriteLine("private:");
        foreach (var (slot, index) in slots.Select((slot, index) => (slot, index)))
        {
            writer.WriteLine($"{GetNativeDirectorCallbackTypeName(index)} {GetNativeDirectorCallbackFieldName(index)} = nullptr;");
        }

        writer.Write(
            """
            };
            """);
        return writer.ToString();
    }

    private static string RenderNativeDirectorMethod(VirtualMethodSlotBinding slot, int index)
    {
        var method = slot.Method;
        var parameterList = string.Join(", ", method.Parameters.Select(parameter => $"{parameter.CppTypeName} {parameter.NativeName}"));
        var arguments = string.Join(", ", method.Parameters.Select(static parameter => parameter.NativeName));
        var writer = new CodeWriter();

        writer.Write($$"""
            {{method.CppReturnTypeName}} {{method.NativeName}}({{parameterList}}){{(method.IsConstMethod ? " const" : string.Empty)}} override
            {
            if ({{GetNativeDirectorCallbackFieldName(index)}} != nullptr)
            {
            """);

        if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.WriteLine($"    {GetNativeDirectorCallbackFieldName(index)}(const_cast<void*>(static_cast<const void*>(this)){(arguments.Length > 0 ? $", {arguments}" : string.Empty)});");
            writer.WriteLine("    return;");
        }
        else
        {
            writer.WriteLine($"    return {GetNativeDirectorCallbackFieldName(index)}(const_cast<void*>(static_cast<const void*>(this)){(arguments.Length > 0 ? $", {arguments}" : string.Empty)});");
        }

        writer.WriteLine("}");
        if (method.IsPureVirtual)
        {
            writer.WriteLine($"throw std::runtime_error(\"SharpCpp callback for pure virtual '{slot.NativeImplementationRecord.NativeName}::{method.NativeName}' was not connected.\");");
        }
        else if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.WriteLine($"{slot.NativeImplementationRecord.CppQualifiedName}::{method.NativeName}({arguments});");
        }
        else
        {
            writer.WriteLine($"return {slot.NativeImplementationRecord.CppQualifiedName}::{method.NativeName}({arguments});");
        }

        writer.WriteLine("}");
        return writer.ToString();
    }

    private static string RenderNativeConnectDirector(RecordBinding record, IReadOnlyList<VirtualMethodSlotBinding> slots)
    {
        var writer = new CodeWriter();
        var directorName = GetNativeDirectorClassName(record);
        var parameterList = string.Join(", ", new[] { $"arg_t<{record.CppQualifiedName}*> self" }.Concat(slots.Select((_, index) => $"void* slot{index}")));
        var arguments = string.Join(", ", slots.Select((_, index) => $"slot{index}"));

        writer.Write($$"""
            SHARPCPP_EXPORT ret_t<void> sharpcpp_e2e_{{SanitizeNativeEntrypointName(record.NativeName)}}_connect_director({{parameterList}})
            {
            auto* director = dynamic_cast<{{directorName}}*>(self);
            if (director != nullptr)
            {
                director->connect_director({{arguments}});
            }
            }
            """);
        return writer.ToString();
    }

    private static string RenderNativeDelete(RecordBinding record, bool requiresDirector)
    {
        if (!requiresDirector)
        {
            return $$"""
                SHARPCPP_EXPORT ret_t<void> sharpcpp_e2e_{{SanitizeNativeEntrypointName(record.NativeName)}}_delete(arg_t<{{record.CppQualifiedName}}*> self)
                {
                MarshallerInvoker::destroy<{{record.CppQualifiedName}}>(self);
                }
                """;
        }

        var directorName = GetNativeDirectorClassName(record);
        return $$"""
            SHARPCPP_EXPORT ret_t<void> sharpcpp_e2e_{{SanitizeNativeEntrypointName(record.NativeName)}}_delete(arg_t<{{record.CppQualifiedName}}*> self)
            {
            if (auto* director = dynamic_cast<{{directorName}}*>(self))
            {
                delete director;
                return;
            }

            delete self;
            }
            """;
    }

    private static string RenderNativeClone(RecordBinding record)
    {
        return $$"""
            SHARPCPP_EXPORT ret_t<{{record.CppQualifiedName}}*> sharpcpp_e2e_{{SanitizeNativeEntrypointName(record.NativeName)}}_clone(arg_t<{{record.CppQualifiedName}}*> self)
            {
            Marshaller<{{record.CppQualifiedName}}*> self_marshaller;
            auto* instance = static_cast<{{record.CppQualifiedName}}*>(self_marshaller.ConvertToCpp(self));
            return detail::TryClone<{{record.CppQualifiedName}}>(instance, 0);
            }
            """;
    }

    private static string RenderNativeMethodInvocation(FunctionBinding method, string abiArguments, string memberSignature)
    {
        if (string.IsNullOrWhiteSpace(method.NativeDispatchRecordCppTypeName))
        {
            return method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                ? $$"""
                    MarshallerInvoker::invoke<FT>(&{{method.OwningRecordCppTypeName}}::{{method.NativeName}}, self{{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)}});
                    """
                : $$"""
                    return MarshallerInvoker::invoke<FT>(&{{method.OwningRecordCppTypeName}}::{{method.NativeName}}, self{{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)}});
                    """;
        }

        return $$"""
                Marshaller<{{method.OwningRecordCppTypeName}}*> self_marshaller;
                auto* instance = static_cast<{{method.OwningRecordCppTypeName}}*>(self_marshaller.ConvertToCpp(self));
                auto* dispatch = dynamic_cast<{{method.NativeDispatchRecordCppTypeName}}*>(instance);
                if (dispatch == nullptr)
                {
                throw std::runtime_error("SharpCpp failed to adjust the native handle for secondary-base dispatch.");
                }

                using DispatchFT = {{memberSignature.Replace(method.OwningRecordCppTypeName + "::*", method.NativeDispatchRecordCppTypeName + "::*", StringComparison.Ordinal)}};
                {{(method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                    ? $"MarshallerInvoker::invoke_instance(static_cast<DispatchFT>(&{method.NativeDispatchRecordCppTypeName}::{method.NativeName}), dispatch{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});"
                    : $"return MarshallerInvoker::invoke_instance(static_cast<DispatchFT>(&{method.NativeDispatchRecordCppTypeName}::{method.NativeName}), dispatch{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});")}}
                """;
    }

    private static string GetNativeDirectorClassName(RecordBinding record)
    {
        return $"__{SanitizeNativeEntrypointName(record.CppQualifiedName)}_director";
    }

    private static string BuildNativeDirectorCallbackSignature(FunctionBinding method)
    {
        var parameterList = string.Join(", ", new[] { "void* self" }.Concat(method.Parameters.Select(parameter => $"arg_t<{parameter.CppTypeName}>")));
        return $"ret_t<{method.CppReturnTypeName}> (*)({parameterList})";
    }

    private static string GetNativeDirectorCallbackTypeName(int index)
    {
        return $"callback_{index}_t";
    }

    private static string GetNativeDirectorCallbackFieldName(int index)
    {
        return $"callback_{index}_";
    }

    private static string RenderNativeProtectedMethodHelper(RecordBinding record, FunctionBinding method)
    {
        ValidateProtectedMemberSupport(method);

        var parameterList = string.Join(", ", method.Parameters.Select(parameter => $"arg_t<{parameter.CppTypeName}> {parameter.NativeName}"));
        var abiArguments = string.Join(", ", method.Parameters.Select(static parameter => parameter.NativeName));
        var writer = new CodeWriter();
        var helperName = GetNativeProtectedMethodHelperName(method);

        writer.Write($$"""
            {{(method.IsStatic ? "static " : string.Empty)}}ret_t<{{method.CppReturnTypeName}}> {{helperName}}({{parameterList}})
            {
            """);

        if (method.IsStatic)
        {
            writer.WriteLine(method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                ? $"{record.CppQualifiedName}::{method.NativeName}({abiArguments});"
                : $"return {record.CppQualifiedName}::{method.NativeName}({abiArguments});");
        }
        else
        {
            writer.WriteLine(method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                ? $"this->{method.NativeName}({abiArguments});"
                : $"return this->{method.NativeName}({abiArguments});");
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderNativeProtectedFieldHelpers(RecordBinding record, FieldBinding field)
    {
        ValidateProtectedMemberSupport(field);

        var writer = new CodeWriter();
        writer.Write($$"""
            ret_t<{{field.CppTypeName}}> {{GetNativeProtectedFieldGetterHelperName(field)}}()
            {
            return this->{{field.NativeName}};
            }
            """);

        if (!field.IsReadOnly)
        {
            writer.WriteLine();
            writer.Write($$"""
                void {{GetNativeProtectedFieldSetterHelperName(field)}}(arg_t<{{field.CppTypeName}}> value)
                {
                this->{{field.NativeName}} = value;
                }
                """);
        }

        return writer.ToString();
    }

    private static string RenderNativeProtectedMethodInvocation(BindingCompilation compilation, FunctionBinding method, string abiArguments, string memberSignature)
    {
        var record = compilation.Records.First(candidate => string.Equals(candidate.CppQualifiedName, method.OwningRecordCppTypeName, StringComparison.Ordinal));
        return $$"""
                if (auto* director = dynamic_cast<{{GetNativeDirectorClassName(record)}}*>(self))
                {
                {{(method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                    ? $"director->{GetNativeProtectedMethodHelperName(method)}({abiArguments}); return;"
                    : $"return director->{GetNativeProtectedMethodHelperName(method)}({abiArguments});")}}
                }

                throw std::runtime_error("SharpCpp protected member access requires a director-backed instance.");
                """;
    }

    private static string RenderNativeProtectedFieldGetInvocation(BindingCompilation compilation, FieldBinding field)
    {
        var record = compilation.Records.First(candidate => string.Equals(candidate.CppQualifiedName, field.OwningRecordCppTypeName, StringComparison.Ordinal));
        return $$"""
                if (auto* director = dynamic_cast<{{GetNativeDirectorClassName(record)}}*>(self))
                {
                return director->{{GetNativeProtectedFieldGetterHelperName(field)}}();
                }

                throw std::runtime_error("SharpCpp protected field access requires a director-backed instance.");
                """;
    }

    private static string RenderNativeProtectedFieldSetInvocation(BindingCompilation compilation, FieldBinding field)
    {
        var record = compilation.Records.First(candidate => string.Equals(candidate.CppQualifiedName, field.OwningRecordCppTypeName, StringComparison.Ordinal));
        return $$"""
                if (auto* director = dynamic_cast<{{GetNativeDirectorClassName(record)}}*>(self))
                {
                director->{{GetNativeProtectedFieldSetterHelperName(field)}}(value);
                return;
                }

                throw std::runtime_error("SharpCpp protected field access requires a director-backed instance.");
                """;
    }

    private static string GetNativeProtectedMethodHelperName(FunctionBinding method)
    {
        return $"__call_{SanitizeManagedToken(method.ImportMethodName)}";
    }

    private static string GetNativeProtectedFieldGetterHelperName(FieldBinding field)
    {
        return $"__get_{SanitizeManagedToken(field.ManagedName)}";
    }

    private static string GetNativeProtectedFieldSetterHelperName(FieldBinding field)
    {
        return $"__set_{SanitizeManagedToken(field.ManagedName)}";
    }

    private static void ValidateProtectedMemberSupport(FunctionBinding method)
    {
        if (method.Parameters.Any(parameter => parameter.Direction is not BindingParameterDirection.In || parameter.AbiKind is not BindingParameterAbiKind.Value))
        {
            throw new NotSupportedException($"Protected member '{method.ManagedName}' only supports in-value parameters in this slice.");
        }
    }

    private static void ValidateProtectedMemberSupport(FieldBinding field)
    {
        if (field.Type.Kind == BindingTypeKind.Handle)
        {
            throw new NotSupportedException($"Protected field '{field.ManagedName}' does not support handle-typed storage in this slice.");
        }
    }

    private static string RenderNativeCallbaseHelper(RecordBinding record, VirtualMethodSlotBinding slot)
    {
        var method = slot.Method;
        var parameterList = string.Join(", ", method.Parameters.Select(parameter => $"{parameter.CppTypeName} {parameter.NativeName}"));
        var arguments = string.Join(", ", method.Parameters.Select(static parameter => parameter.NativeName));
        var directorName = GetNativeDirectorClassName(record);
        var writer = new CodeWriter();

        writer.Write($$"""
            static {{method.CppReturnTypeName}} {{GetNativeCallbaseHelperName(method)}}({{record.CppQualifiedName}}* self{{(method.Parameters.Count > 0 ? $", {parameterList}" : string.Empty)}})
            {
            auto* director = static_cast<{{directorName}}*>(self);
            """);

        if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.WriteLine($"    director->{slot.NativeImplementationRecord.CppQualifiedName}::{method.NativeName}({arguments});");
        }
        else
        {
            writer.WriteLine($"    return director->{slot.NativeImplementationRecord.CppQualifiedName}::{method.NativeName}({arguments});");
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string GetNativeCallbaseHelperName(FunctionBinding method)
    {
        return $"__callbase_{SanitizeManagedToken(method.ManagedName)}";
    }

    private static string GetNativeCallbaseEntrypointName(RecordBinding record, FunctionBinding method)
    {
        return $"sharpcpp_e2e_{record.NativeName}_callbase_{method.NativeName}";
    }

    private static string GetManagedCallbaseImportMethodName(FunctionBinding function)
    {
        return $"__CallBase_{function.ImportMethodName}";
    }


    private static string RenderManagedWrapperMethod(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var returnTypeName = GetManagedPublicReturnTypeName(function.ReturnType);
        var importMethodName = GetManagedImportMethodName(function);

        if (HasUndefaultableParams(function))
        {
            WriteUndefaultableOverloads(compilation, writer, function, importMethodName, []);
            return writer.ToString();
        }

        if (HasUnexpressibleDefaultParams(function))
        {
            WriteManagedUnexpressibleDefaultConvenienceOverloads(
                writer,
                function,
                signaturePrefix: $"public static {returnTypeName} {function.ManagedName}",
                invocationPrefix: string.Empty,
                invocationTarget: function.ManagedName,
                returnTypeName: returnTypeName,
                isConstructor: false);
        }

        var parameterList = RenderManagedPublicParameterList(function.Parameters);
        var invocationPlan = BuildManagedInvocationPlan(compilation, function.Parameters);
        var invokeArguments = string.Join(", ", invocationPlan.Arguments);

        writer.Write($$"""
            public static {{returnTypeName}} {{function.ManagedName}}({{parameterList}})
            {
            """);

        foreach (var statement in invocationPlan.PreCallStatements)
        {
            writer.Write(statement);
        }

        if (HasUnexpressibleDefaultParams(function))
        {
            WriteUnexpressibleDefaultCascade(compilation, writer, function, importMethodName, []);
        }
        else
        {

        if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.Write($$"""
                __PInvoke.{{importMethodName}}({{invokeArguments}});
                """);

            foreach (var statement in invocationPlan.PostCallStatements)
            {
                writer.Write(statement);
            }
        }
        else if (function.ReturnType.Kind == BindingTypeKind.Handle)
        {
            writer.Write($$"""
                var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                """);

            foreach (var statement in invocationPlan.PostCallStatements)
            {
                writer.Write(statement);
            }

            writer.Write($$"""
                return {{BuildManagedHandleWrap(compilation, function.ReturnType, "__result", function.ReturnsOwnedHandle)}};
                """);
        }
        else if (function.ReturnType.MarshallerTypeName is not null)
        {
            WriteManagedReturnWithMarshallerConversion(writer, function, importMethodName, invokeArguments, invocationPlan);
        }
        else
        {
            writer.Write($$"""
                var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                """);

            foreach (var statement in invocationPlan.PostCallStatements)
            {
                writer.Write(statement);
            }

            writer.Write(
                """
                return __result;
                """);
        }

        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedImportDeclaration(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var parameterList = string.Join(
            ", ",
            function.Parameters.Select(RenderManagedParameter));
        var returnTypeName = GetManagedImportTypeName(function.ReturnType);
        var returnAttribute = GetManagedReturnAttribute(function.ReturnType);
        var importMethodName = GetManagedImportMethodName(function);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} {importMethodName}({parameterList});";

        writer.Write(
            $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderAliasBindings(BindingCompilation? compilation = null, bool includeRegistry = false)
    {
        if (compilation is null || !includeRegistry)
        {
            return "// <auto-generated />\n// No alias glue is required for this bootstrap pipeline.\n";
        }

        var polymorphicConcreteRecords = compilation.Records
            .Where(static record => record.IsPolymorphic && !record.IsEffectivelyAbstract && !record.HasMetadata(MetadataKeys.IgnoreReason))
            .ToArray();
        if (polymorphicConcreteRecords.Length == 0)
        {
            return "// <auto-generated />\n// No registry contents to generate.\n";
        }

        var writer = new CodeWriter();
        writer.Write(
            """
            // <auto-generated />
            #nullable enable
            using System.Runtime.InteropServices;
            """);

        writer.Write($$"""

            namespace {{compilation.ManagedNamespace}}
            {
            public static partial class {{compilation.ManagedClassName}}
            {
            """);
        writer.Write(RenderNativeObjectRegistryInitializer(compilation, polymorphicConcreteRecords));
        writer.Write(
            """
            }
            }
            """);
        return writer.ToString();
    }

    private static string RenderRecord(BindingCompilation compilation, RecordBinding record)
    {
        if (record.IsGeneric)
        {
            return RenderGenericRecord(compilation, record);
        }

        var writer = new CodeWriter();
        var canEmitDeleteThunk = !record.GetMetadata<bool>(MetadataKeys.NonPublicDestructor);
        var pinvokeDeclarations = new List<string>();
        if (canEmitDeleteThunk)
        {
            pinvokeDeclarations.Add(RenderManagedDeleteImportDeclaration(compilation, record));
        }
        var requiresDirector = record.RequiresDirector(compilation);
        var virtualSlots = record.GetEffectiveVirtualMethodSlots(compilation);
        var isEffectivelyAbstract = record.IsEffectivelyAbstract ||
            virtualSlots.Any(static slot => slot.Method.IsPureVirtual) ||
            record.HasUnimplementedAbstractBaseMethods(compilation);
        if (record.IsPolymorphic)
        {
            pinvokeDeclarations.Add(RenderManagedStaticTypeIdImportDeclaration(compilation, record));
            pinvokeDeclarations.Add(RenderManagedNativeObjectMetadataImportDeclaration(compilation, record));
        }
        if (requiresDirector)
        {
            pinvokeDeclarations.Add(RenderManagedConnectDirectorImportDeclaration(compilation, record, virtualSlots));
        }

        WriteDocumentationComment(writer, record);
        var classModifier = isEffectivelyAbstract ? "abstract " : string.Empty;
        var hidesStaticHelpers = !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) && 
            !record.ManagedBaseTypeName.Contains('.', StringComparison.Ordinal);
        var staticHelperModifier = hidesStaticHelpers ? "new " : string.Empty;
        var cacheFieldModifier = hidesStaticHelpers ? "new " : string.Empty;
        var interfaceNames = record.GetSecondaryBaseRecords(compilation)
            .Where(baseRecord => !baseRecord.IsIgnored() && baseRecord.GetRenderableInterfaceMethods(compilation).Count > 0)
            .Select(static baseRecord => baseRecord.GetManagedInterfaceName())
            .Cast<string>()
            .ToArray();
        var canEmitClone = CanEmitClone(record, isEffectivelyAbstract, canEmitDeleteThunk);
        var baseTypes = new List<string>
        {
            string.IsNullOrWhiteSpace(record.ManagedBaseTypeName)
                ? "global::SharpCpp.Support.NativeObject"
                : record.ManagedBaseTypeName
        };
        baseTypes.AddRange(interfaceNames);
        if (canEmitClone)
        {
            baseTypes.Add("global::System.ICloneable");
        }
        var baseClause = $" : {string.Join(", ", baseTypes)}";
        writer.Write(
            $$"""
            public {{classModifier}}partial class {{record.ManagedName}}{{baseClause}}{{(record.IsPolymorphic ? $", global::SharpCpp.Support.INativeObjectRegistration<{record.ManagedName}>" : string.Empty)}}
            {
            {{cacheFieldModifier}}internal static readonly {{GetInstanceCacheFieldTypeName(record)}} s_instanceCache = {{GetInstanceCacheFactoryExpression(record)}};

            protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => s_instanceCache;

            protected internal {{record.ManagedName}}(nint handle, bool ownsHandle = false)
                : base(handle, ownsHandle)
            {
            if (!IsInvalid)
            {
                PrepareForOwnedRegistration();
                s_instanceCache.Register(this);
            }
            }

            {{(string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) && !record.HasOwnManagedMemberNamed("Handle") ? "public nint Handle => DangerousGetHandle();" : string.Empty)}}
            {{(string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) && !record.HasOwnManagedMemberNamed("OwnsHandle") ? "public bool OwnsHandle => OwnsNativeHandle;" : string.Empty)}}
            {{(string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) && !record.HasOwnManagedMemberNamed("IsNull") ? "public bool IsNull => IsClosed || IsInvalid;" : string.Empty)}}

            {{staticHelperModifier}}public static {{record.ManagedName}} CreateManagedWrapper(nint handle, bool ownsHandle)
            {
            if (handle == 0)
                {{(isEffectivelyAbstract ? "return null!;" : $"return new {record.ManagedName}(0, ownsHandle);")}}

            {{(record.IsPolymorphic ? $"{compilation.ManagedClassName}.__NativeObjectRegistryInitializer.EnsureRegistered();" : string.Empty)}}
            if (s_instanceCache.TryGetExisting(handle, ownsHandle, out var existing) && existing is {{record.ManagedName}} cached)
                return cached;

            {{(record.IsPolymorphic
                ? $"return ({record.ManagedName})global::SharpCpp.Support.NativeObjectRegistry.Create<{record.ManagedName}>(handle, ownsHandle);"
                : isEffectivelyAbstract
                    ? "return null!;"
                    : $"return new {record.ManagedName}(handle, ownsHandle);")}}
            }
            """);

        if (record.Members.OfType<FunctionBinding>().Any(static function => function.OperatorToken is "==" or "!="))
        {
            writer.WriteLine();
            writer.Write($$"""
                public override bool Equals(object? obj)
                {
                return obj is {{record.ManagedName}} other && this == other;
                }

                public override int GetHashCode()
                {
                return Handle.GetHashCode();
                }
                """);
        }

        writer.WriteLine();
        writer.Write(RenderManagedDispose(record, canEmitDeleteThunk));

        if (canEmitClone)
        {
            writer.WriteLine();
            writer.Write(RenderManagedCloneMethod(compilation, record));
            pinvokeDeclarations.Add(RenderManagedCloneImportDeclaration(compilation, record));
        }

        var accessorProperties = CollectAccessorProperties(record);
        var accessorMethodIds = accessorProperties
            .SelectMany(static property => new[] { property.Getter?.SymbolId, property.Setter?.SymbolId })
            .Where(static symbolId => !string.IsNullOrWhiteSpace(symbolId))
            .ToHashSet(StringComparer.Ordinal);
        var lastRecordMemberWasIgnoredStub = false;

        void WriteRecordMemberSeparator(bool isIgnoredStub)
        {
            writer.WriteLine();
            if (lastRecordMemberWasIgnoredStub && !isIgnoredStub)
            {
                writer.WriteLine();
            }

            lastRecordMemberWasIgnoredStub = isIgnoredStub;
        }

        void CloseIgnoredStubCluster()
        {
            if (lastRecordMemberWasIgnoredStub)
            {
                writer.WriteLine();
                lastRecordMemberWasIgnoredStub = false;
            }
        }

        foreach (var constructor in record.Members.OfType<FunctionBinding>().Where(static function => function.IsConstructor).OrderBy(static function => function.Parameters.Count))
        {
            var isIgnoredStub = constructor.HasMetadata(MetadataKeys.IgnoreReason);
            WriteRecordMemberSeparator(isIgnoredStub);
            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(constructor));
            }
            else
            {
                writer.Write(RenderManagedConstructor(record, constructor, requiresDirector));
                pinvokeDeclarations.Add(RenderManagedConstructorImportDeclaration(compilation, constructor));
                if (HasUnexpressibleDefaultParams(constructor) || HasUndefaultableParams(constructor))
                {
                    pinvokeDeclarations.Add(RenderManagedUnexpressibleDefaultConstructorImports(compilation, constructor));
                }
            }
        }

        foreach (var property in accessorProperties.OrderBy(static property => property.PropertyName, StringComparer.Ordinal))
        {
            WriteRecordMemberSeparator(isIgnoredStub: false);
            writer.Write(RenderManagedAccessorProperty(compilation, property));

            if (property.Getter is not null)
            {
                pinvokeDeclarations.Add(RenderManagedInstanceImportDeclaration(compilation, property.Getter, record.ManagedName));
            }

            if (property.Setter is not null)
            {
                pinvokeDeclarations.Add(RenderManagedInstanceImportDeclaration(compilation, property.Setter, record.ManagedName));
            }
        }

        foreach (var method in record.Members.OfType<FunctionBinding>().Where(function => !function.IsConstructor && !function.IsStatic && !function.IsConversionOperator && function.OperatorToken is null && !accessorMethodIds.Contains(function.SymbolId)).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            var isIgnoredStub = method.HasMetadata(MetadataKeys.IgnoreReason);
            WriteRecordMemberSeparator(isIgnoredStub);
            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(method));
            }
            else
            {
                var isDirectorVirtual = requiresDirector && method.IsVirtual;
                writer.Write(RenderManagedInstanceMethod(compilation, record, method, isDirectorVirtual));
                if (isDirectorVirtual)
                {
                    pinvokeDeclarations.Add(RenderManagedCallbaseImportDeclaration(compilation, method, record.ManagedName));
                    if (HasUnexpressibleDefaultParams(method) || HasUndefaultableParams(method))
                    {
                        pinvokeDeclarations.Add(RenderManagedUnexpressibleDefaultInstanceImports(compilation, method, record.ManagedName, useCallbase: true));
                    }
                }
                else
                {
                    pinvokeDeclarations.Add(RenderManagedInstanceImportDeclaration(compilation, method, record.ManagedName));
                    if (HasUnexpressibleDefaultParams(method) || HasUndefaultableParams(method))
                    {
                        pinvokeDeclarations.Add(RenderManagedUnexpressibleDefaultInstanceImports(compilation, method, record.ManagedName));
                    }
                }
            }
        }

        foreach (var method in record.Members.OfType<FunctionBinding>().Where(static function => function.IsStatic && !function.IsConversionOperator && function.OperatorToken is null).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            var isIgnoredStub = method.HasMetadata(MetadataKeys.IgnoreReason);
            WriteRecordMemberSeparator(isIgnoredStub);
            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(method));
            }
            else
            {
                writer.Write(RenderManagedStaticMethod(compilation, method));
                pinvokeDeclarations.Add(RenderManagedStaticImportDeclaration(compilation, method));
                if (HasUnexpressibleDefaultParams(method) || HasUndefaultableParams(method))
                {
                    pinvokeDeclarations.Add(RenderManagedUnexpressibleDefaultStaticImports(compilation, method));
                }
            }
        }

        foreach (var method in record.Members.OfType<FunctionBinding>().Where(static function => function.IsConversionOperator || function.OperatorToken is not null).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            var isIgnoredStub = method.HasMetadata(MetadataKeys.IgnoreReason);
            WriteRecordMemberSeparator(isIgnoredStub);
            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(method));
            }
            else
            {
                writer.Write(RenderManagedOperator(compilation, record, method));
                pinvokeDeclarations.Add(RenderManagedOperatorImportDeclaration(compilation, method, record.ManagedName));
            }
        }

        foreach (var field in record.Members.OfType<FieldBinding>().OrderBy(static field => field.ManagedName, StringComparer.Ordinal))
        {
            if (record.HasOwnManagedNonFieldMemberNamed(field.ManagedName))
            {
                continue;
            }

            var isIgnoredStub = field.HasMetadata(MetadataKeys.IgnoreReason);
            WriteRecordMemberSeparator(isIgnoredStub);
            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(field));
            }
            else
            {
                writer.Write(RenderManagedProperty(compilation, field));
                pinvokeDeclarations.Add(RenderManagedFieldGetterImportDeclaration(compilation, field, record.ManagedName));

                if (!field.IsReadOnly)
                {
                    pinvokeDeclarations.Add(RenderManagedFieldSetterImportDeclaration(compilation, field, record.ManagedName));
                }
            }
        }

        CloseIgnoredStubCluster();

        writer.WriteLine();
        if (record.IsPolymorphic)
        {
            writer.Write(
                $$"""
                {{staticHelperModifier}}internal static nuint GetManagedTypeId()
                {
                return __PInvoke.GetStaticTypeId();
                }

                static nuint global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>.RegistrationDynamicTypeId => GetManagedTypeId();

                static global::System.Type global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>.RegistrationExpectedType => typeof({{record.ManagedName}});

                static global::SharpCpp.Support.INativeInstanceCache global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>.RegistrationInstanceCache => s_instanceCache;

                static global::System.Func<nint, bool, global::SharpCpp.Support.NativeObject>? global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>.RegistrationCreateFactory => {{(isEffectivelyAbstract ? "null" : $"static (handle, ownsHandle) => new {record.ManagedName}(handle, ownsHandle)")}};

                static global::SharpCpp.Support.NativeObjectMetadata global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>.RegistrationGetMetadata(nint handle)
                {
                __PInvoke.GetNativeObjectMetadata(handle, out var resolvedHandle, out var dynamicTypeId);
                return new global::SharpCpp.Support.NativeObjectMetadata(resolvedHandle, dynamicTypeId);
                }
                """);
            writer.WriteLine();
        }

        if (requiresDirector)
        {
            foreach (var slot in virtualSlots)
            {
                writer.WriteLine();
                writer.Write(RenderManagedVirtualCallbackMembers(compilation, slot));
            }

            writer.WriteLine();
            writer.Write(RenderManagedConnectDirector(record, virtualSlots));
            writer.WriteLine();
        }

        var hasGeneratedBase = !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) && 
            !record.ManagedBaseTypeName.Contains('.', StringComparison.Ordinal);
        writer.Write(
            hasGeneratedBase
            ? $$"""
            new internal static partial class __PInvoke
            {
            """
            : $$"""
            internal static partial class __PInvoke
            {
            """);

        foreach (var declaration in pinvokeDeclarations)
        {
            writer.WriteLine();
            writer.Write(declaration);
        }

        writer.WriteLine();
        writer.Write(
            """
            }
            """);

        writer.WriteLine();
        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedInstanceMethod(BindingCompilation compilation, RecordBinding record, FunctionBinding function, bool useCallbase = false)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var returnTypeName = GetManagedPublicReturnTypeName(function.ReturnType);
        var importMethodName = useCallbase
            ? GetManagedCallbaseImportMethodName(function)
            : GetManagedImportMethodName(function);
        var memberModifier = GetManagedInstanceMethodModifier(compilation, record, function);

        if (memberModifier.Contains("abstract ", StringComparison.Ordinal))
        {
            var parameterList = RenderManagedPublicParameterList(function.Parameters);
            writer.Write($$"""
                {{GetManagedVisibilityNormalized(compilation, record, function)}} {{memberModifier}}{{returnTypeName}} {{function.ManagedName}}({{parameterList}});
                """);
            return writer.ToString();
        }

        if (HasUndefaultableParams(function))
        {
            WriteUndefaultableOverloads(compilation, writer, function, importMethodName, ["this"]);
            return writer.ToString();
        }

        if (HasUnexpressibleDefaultParams(function))
        {
            WriteManagedUnexpressibleDefaultConvenienceOverloads(
                writer,
                function,
                signaturePrefix: $"{GetManagedVisibilityNormalized(compilation, record, function)} {memberModifier}{returnTypeName} {function.ManagedName}",
                invocationPrefix: string.Empty,
                invocationTarget: function.ManagedName,
                returnTypeName: returnTypeName,
                isConstructor: false);
        }

        var parameterListFull = RenderManagedPublicParameterList(function.Parameters);

        writer.Write($$"""
            {{GetManagedVisibilityNormalized(compilation, record, function)}} {{memberModifier}}{{returnTypeName}} {{function.ManagedName}}({{parameterListFull}})
            {
            """);

        if (HasUnexpressibleDefaultParams(function))
        {
            WriteUnexpressibleDefaultCascade(compilation, writer, function, importMethodName, ["this"]);
        }
        else
        {
            var invocationPlan = BuildManagedInvocationPlan(compilation, function.Parameters);
            var invokeArguments = string.Join(", ", new[] { "this" }.Concat(invocationPlan.Arguments));

            foreach (var statement in invocationPlan.PreCallStatements)
            {
                writer.Write(statement);
            }

            if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.Write($$"""
                    __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }
            }
            else if (function.ReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.Write($$"""
                    var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

                writer.Write($$"""
                    return {{BuildManagedHandleWrap(compilation, function.ReturnType, "__result", function.ReturnsOwnedHandle)}};
                    """);
            }
            else if (function.ReturnType.MarshallerTypeName is not null)
            {
                WriteManagedReturnWithMarshallerConversion(writer, function, importMethodName, invokeArguments, invocationPlan);
            }
            else
            {
                writer.Write($$"""
                    var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

                writer.Write(
                    """
                    return __result;
                    """);
            }
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static bool ShouldUseNewModifier(BindingCompilation compilation, RecordBinding record, string managedName)
    {
        if (string.IsNullOrWhiteSpace(record.ManagedBaseTypeName))
        {
            return false;
        }

        var baseRecord = compilation.Records.FirstOrDefault(candidate => string.Equals(candidate.ManagedName, record.ManagedBaseTypeName, StringComparison.Ordinal));
        return baseRecord is not null &&
               baseRecord.Members.OfType<FunctionBinding>().Any(candidate => !candidate.IsIgnored() && string.Equals(candidate.ManagedName, managedName, StringComparison.Ordinal));
    }

    private static string RenderManagedDispose(RecordBinding record, bool canEmitDeleteThunk)
    {
        if (!record.EmitDefaultNativeReleaseOverride)
        {
            return string.Empty;
        }

        if (!canEmitDeleteThunk)
        {
            return """
                protected override bool ReleaseNativeHandle(nint handle)
                {
                return true;
                }
                """;
        }

        return """
            protected override bool ReleaseNativeHandle(nint handle)
            {
            if (OwnsNativeHandle)
            {
                __PInvoke.Delete(handle);
            }
            return true;
            }
            """;
    }

    private static string RenderManagedDeleteImportDeclaration(BindingCompilation compilation, RecordBinding record)
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "sharpcpp_e2e_{{SanitizeNativeEntrypointName(record.NativeName)}}_delete", StringMarshalling = StringMarshalling.Utf8)]
            internal static partial void Delete(nint self);
            """;
    }

    private static string RenderManagedCloneImportDeclaration(BindingCompilation compilation, RecordBinding record)
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "sharpcpp_e2e_{{SanitizeNativeEntrypointName(record.NativeName)}}_clone", StringMarshalling = StringMarshalling.Utf8)]
            internal static partial nint CloneCopyCtor({{record.ManagedName}} self);
            """;
    }

    private static string RenderManagedCloneMethod(BindingCompilation compilation, RecordBinding record)
    {
        var writer = new CodeWriter();

        if (!record.HasOwnManagedNonFieldMemberNamed("Clone"))
        {
            var hidesBaseClone = InheritsCloneInterface(compilation, record);
            writer.Write($$"""
                public {{(hidesBaseClone ? "new " : string.Empty)}}{{record.ManagedName}} Clone()
                {
                var __result = __PInvoke.CloneCopyCtor(this);
                if (__result == 0)
                {
                throw new NotSupportedException("SharpCpp could not clone this native object because the native type is not copy-constructible.");
                }
                return {{record.ManagedName}}.CreateManagedWrapper(__result, ownsHandle: true);
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
                var __result = __PInvoke.CloneCopyCtor(this);
                if (__result == 0)
                {
                throw new NotSupportedException("SharpCpp could not clone this native object because the native type is not copy-constructible.");
                }
                return {{record.ManagedName}}.CreateManagedWrapper(__result, ownsHandle: true);
                }
                """);
        }

        return writer.ToString();
    }

    private static string RenderManagedAccessorProperty(BindingCompilation compilation, AccessorProperty property)
    {
        var writer = new CodeWriter();
        var type = property.Getter?.ReturnType ?? property.Setter!.Parameters[0].Type;
        var propertyTypeName = GetManagedPublicReturnTypeName(type);

        writer.Write($$"""
            {{GetManagedVisibility(GetAccessorPropertyAccessModifier(property))}} {{propertyTypeName}} {{property.PropertyName}}
            {
            """);

        if (property.Getter is not null)
        {
            if (property.Getter.ReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.Write($$"""
                    get
                    {
                    return {{BuildManagedHandleWrap(compilation, property.Getter.ReturnType, $"__PInvoke.{GetManagedImportMethodName(property.Getter)}(this)", ownsHandle: false)}};
                    }
                    """);
            }
            else
            {
                writer.Write($$"""
                    get
                    {
                    return __PInvoke.{{GetManagedImportMethodName(property.Getter)}}(this);
                    }
                    """);
            }
        }

        if (property.Setter is not null)
        {
            var requiresWrapping = RequiresHandleWrapping(property.Setter.Parameters[0].Type);
            var setterArgument = requiresWrapping
                ? "global::SharpCpp.Support.NativeObject.GetHandle(value)"
                : "value";
            var setterKeepAlive = requiresWrapping
                ? "\n            global::System.GC.KeepAlive(value);"
                : string.Empty;
            writer.Write($$"""
                set
                {
                __PInvoke.{{GetManagedImportMethodName(property.Setter)}}(this, {{setterArgument}});{{setterKeepAlive}}
                }
                """);
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static IReadOnlyList<AccessorProperty> CollectAccessorProperties(RecordBinding record)
    {
        var getters = new Dictionary<string, FunctionBinding>(StringComparer.Ordinal);
        var setters = new Dictionary<string, FunctionBinding>(StringComparer.Ordinal);

        foreach (var method in record.Members.OfType<FunctionBinding>())
        {
            if (method.HasMetadata(MetadataKeys.IgnoreReason))
            {
                continue;
            }

            if (!method.TryGetMetadata<AccessorPropertyAnnotation>(MetadataKeys.AccessorProperty, out var accessor) || accessor is null)
            {
                continue;
            }

            if (accessor.Kind == AccessorPropertyKind.Getter)
            {
                getters[accessor.PropertyName] = method;
            }
            else
            {
                setters[accessor.PropertyName] = method;
            }
        }

        return getters.Keys
            .Concat(setters.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(propertyName => new AccessorProperty(
                propertyName,
                getters.TryGetValue(propertyName, out var getter) ? getter : null,
                setters.TryGetValue(propertyName, out var setter) ? setter : null))
            .ToArray();
    }

    private static string RenderManagedConstructor(RecordBinding record, FunctionBinding function, bool requiresDirector)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var constructorName = function.Parent is RecordBinding owningRecord
            ? owningRecord.ManagedName
            : NormalizeEnumManagedTypeName(function.ManagedName);
        var importMethodName = GetManagedImportMethodName(function);

        if (HasUndefaultableParams(function))
        {
            WriteUndefaultableConstructorOverloads(writer, record, function, constructorName, importMethodName, requiresDirector);
            return writer.ToString();
        }

        if (HasUnexpressibleDefaultParams(function))
        {
            WriteManagedUnexpressibleDefaultConvenienceOverloads(
                writer,
                function,
                signaturePrefix: $"{GetManagedVisibility(function.AccessModifier)} {constructorName}",
                invocationPrefix: ": this",
                invocationTarget: string.Empty,
                returnTypeName: null,
                isConstructor: true);
        }

        var parameterList = RenderManagedPublicParameterList(function.Parameters);
        var invokeArguments = string.Join(", ", function.Parameters.Select(RenderManagedInvocationArgument));
        var hasHandleParams = function.Parameters.Any(static parameter => parameter.Type.Kind == BindingTypeKind.Handle);
        var hasUnexpressibleDefaults = HasUnexpressibleDefaultParams(function);

        if (hasHandleParams || hasUnexpressibleDefaults)
        {
            var factoryName = $"__CreateHandle_{importMethodName}";

            writer.Write($$"""
                {{GetManagedVisibility(function.AccessModifier)}} {{constructorName}}({{parameterList}})
                    : this({{factoryName}}({{string.Join(", ", function.Parameters.Select(RenderManagedForwardingArgument))}}), true)
                {
                {{(requiresDirector ? "ConnectDirector();" : string.Empty)}}
                }

                private static nint {{factoryName}}({{parameterList}})
                {
                """);

            if (hasUnexpressibleDefaults)
            {
                WriteUnexpressibleDefaultConstructorCascade(writer, function, importMethodName);
            }
            else
            {
                var invocationPlan = BuildManagedInvocationPlan(null!, function.Parameters);
                writer.Write($$"""
                    var __handle = __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

                writer.Write(
                    """
                    return __handle;
                    """);
            }

            writer.Write(
                """
                }
                """);
        }
        else
        {
            writer.Write($$"""
                {{GetManagedVisibility(function.AccessModifier)}} {{constructorName}}({{parameterList}})
                    : this(__PInvoke.{{importMethodName}}({{invokeArguments}}), true)
                {
                {{(requiresDirector ? "ConnectDirector();" : string.Empty)}}
                }
                """);
        }

        return writer.ToString();
    }

    private static void WriteUndefaultableConstructorOverloads(
        CodeWriter writer,
        RecordBinding record,
        FunctionBinding function,
        string constructorName,
        string importMethodName,
        bool requiresDirector)
    {
        var firstU = GetFirstUndefaultableParamIndex(function);
        if (firstU < 0)
        {
            return;
        }

        // =========================================================
        // Overload A: params before first undefaultable index
        // =========================================================
        {
            var beforeParams = function.Parameters.Take(firstU).ToArray();
            var aParamList = string.Join(", ", beforeParams.Select(RenderManagedPublicParameter));
            var aArgNames = string.Join(", ", beforeParams.Select(RenderManagedForwardingArgument));
            var aHasUnexpressible = beforeParams.Any(p => p.HasMetadata(MetadataKeys.UnexpressibleDefault));

            var aFactoryName = $"__CreateHandle_{importMethodName}_OverloadA";

            writer.Write($$"""
                public {{constructorName}}({{aParamList}})
                    : this({{aFactoryName}}({{aArgNames}}), true)
                {
                {{(requiresDirector ? "ConnectDirector();" : string.Empty)}}
                }

                private static nint {{aFactoryName}}({{aParamList}})
                {
                """);

            if (firstU == 0)
            {
                writer.Write($$"""
                    var __handle = __PInvoke.{{GetUnexpressibleVariantImportName(importMethodName, 0)}}();
                    return __handle;
                    """);
            }
            else if (aHasUnexpressible)
            {
                WriteUnexpressibleDefaultConstructorCascade(writer, function, importMethodName);
            }
            else
            {
                var aPlan = BuildManagedInvocationPlan(null!, beforeParams);
                var aInvokeArgs = string.Join(", ", beforeParams.Select(RenderManagedInvocationArgument));
                writer.Write($$"""
                    var __handle = __PInvoke.{{GetUnexpressibleVariantImportName(importMethodName, firstU)}}({{aInvokeArgs}});
                    """);
                foreach (var statement in aPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }
                writer.Write("""
                    return __handle;
                    """);
            }

            writer.Write("""
                }
                """);
            writer.WriteLine();
        }

        // =========================================================
        // Overload B: all params, before/first undefaultable are required
        // =========================================================
        {
            var bParamParts = new List<string>();
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var param = function.Parameters[i];
                if (i < firstU || param.HasMetadata(MetadataKeys.UndefaultableParam))
                {
                    bParamParts.Add(RenderManagedPublicParameterRequired(param));
                }
                else
                {
                    bParamParts.Add(RenderManagedPublicParameter(param));
                }
            }
            var bParamList = string.Join(", ", bParamParts);
            var bArgNames = string.Join(", ", function.Parameters.Select(RenderManagedForwardingArgument));
            var bFactoryName = $"__CreateHandle_{importMethodName}_OverloadB";
            var bHasAfterUnexpressible = function.Parameters.Skip(firstU).Any(p => p.HasMetadata(MetadataKeys.UnexpressibleDefault));

            writer.Write($$"""
                public {{constructorName}}({{bParamList}})
                    : this({{bFactoryName}}({{bArgNames}}), true)
                {
                {{(requiresDirector ? "ConnectDirector();" : string.Empty)}}
                }

                private static nint {{bFactoryName}}({{bParamList}})
                {
                """);

            if (bHasAfterUnexpressible)
            {
                // Cascade only for after-firstU unexpressible defaults.
                // We use a filtered approach: create a projection of the function
                // that only has the after-firstU params, so the standard cascade
                // only generates conditions/variants for those positions.
                // Reuse the cascade but with a builder that adjusts param names.
                var uFirst = GetFirstUndefaultableParamIndex(function);
                var afterDefaultIndices = new List<int>();
                for (var i = uFirst; i < function.Parameters.Count; i++)
                {
                    if (function.Parameters[i].HasMetadata(MetadataKeys.UnexpressibleDefault))
                    {
                        afterDefaultIndices.Add(i);
                    }
                }

                // Write a targeted cascade for the after-params only.
                // Filter defaultIndices to only after-firstU positions, since
                // before-firstU params are non-nullable in Overload B.
                WriteUndefaultableConstructorCascade_Filtered(writer, function, importMethodName, uFirst, afterDefaultIndices);
            }
            else
            {
                var bInvokeArgs = string.Join(", ", function.Parameters.Select(RenderManagedInvocationArgument));
                writer.Write($$"""
                    var __handle = __PInvoke.{{importMethodName}}({{bInvokeArgs}});
                    return __handle;
                    """);
            }

            writer.Write("""
                }
                """);
        }
    }

    private static string RenderManagedConstructorImportDeclaration(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var parameterList = string.Join(
            ", ",
            function.Parameters.Select(RenderManagedParameter));
        var importMethodName = GetManagedImportMethodName(function);

        writer.Write($$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            internal static partial nint {{importMethodName}}({{parameterList}});
            """);
        return writer.ToString();
    }

    private static string RenderManagedConnectDirectorImportDeclaration(
        BindingCompilation compilation,
        RecordBinding record,
        IReadOnlyList<VirtualMethodSlotBinding> slots)
    {
        var parameterList = string.Join(
            ", ",
            new[] { "nint self" }.Concat(slots.Select((_, index) => $"nint slot{index}")));

        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "sharpcpp_e2e_{{SanitizeNativeEntrypointName(record.NativeName)}}_connect_director", StringMarshalling = StringMarshalling.Utf8)]
            internal static partial void ConnectDirector({{parameterList}});
            """;
    }

    private static string RenderManagedConnectDirector(RecordBinding record, IReadOnlyList<VirtualMethodSlotBinding> slots)
    {
        var callbackArguments = slots.Count == 0
            ? string.Empty
            : ", " + string.Join(
                ", ",
                slots.Select(slot => $"{slot.ManagedCallbackOwnerRecord.ManagedName}.{GetVirtualCallbackPointerAccessorName(slot.ManagedCallbackMethod)}(runtimeType)"));

        return $$"""
            private void ConnectDirector()
            {
            var runtimeType = ((object)this).GetType();
            __PInvoke.ConnectDirector(DangerousGetHandle(){{callbackArguments}});
            }
            """;
    }

    private static string RenderManagedVirtualCallbackMembers(BindingCompilation compilation, VirtualMethodSlotBinding slot)
    {
        var method = slot.ManagedCallbackMethod;
        ValidateVirtualCallbackSupport(method);

        var ownerRecord = slot.ManagedCallbackOwnerRecord;
        var writer = new CodeWriter();
        var callbackReturnType = GetManagedImportTypeName(method.ReturnType);
        var returnAttribute = GetManagedReturnAttribute(method.ReturnType);
        var callbackParameterList = string.Join(
            ", ",
            new[] { "nint self" }.Concat(method.Parameters.Select(RenderManagedCallbackParameter)));
        var invocationArguments = string.Join(
            ", ",
            method.Parameters.Select(parameter => BuildManagedCallbackInvocationArgument(compilation, parameter)));

        if (!string.IsNullOrWhiteSpace(returnAttribute))
        {
            writer.WriteLine(returnAttribute);
        }

        writer.Write($$"""
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate {{callbackReturnType}} {{GetVirtualCallbackDelegateName(method)}}({{callbackParameterList}});

            private static readonly {{GetVirtualCallbackDelegateName(method)}} {{GetVirtualCallbackFieldName(method)}} = {{GetVirtualCallbackDispatchName(method)}};
            private static readonly nint {{GetVirtualCallbackPointerFieldName(method)}} = Marshal.GetFunctionPointerForDelegate({{GetVirtualCallbackFieldName(method)}});

            internal static nint {{GetVirtualCallbackPointerAccessorName(method)}}(Type runtimeType)
            {
            return HasManagedOverride(runtimeType, typeof({{ownerRecord.ManagedName}}), nameof({{ownerRecord.ManagedName}}.{{method.ManagedName}}), {{FormatReflectionParameterTypes(method)}})
                ? {{GetVirtualCallbackPointerFieldName(method)}}
                : 0;
            }

            """);

        if (!string.IsNullOrWhiteSpace(returnAttribute))
        {
            writer.WriteLine(returnAttribute);
        }

        writer.Write($$"""
            private static {{callbackReturnType}} {{GetVirtualCallbackDispatchName(method)}}({{callbackParameterList}})
            {
            var owner = {{ownerRecord.ManagedName}}.CreateManagedWrapper(self, ownsHandle: false);
            """);

        var invocation = $"owner.{method.ManagedName}({invocationArguments})";
        if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.Write($$"""
                {{invocation}};
            }
            """);
            return writer.ToString();
        }

        if (method.ReturnType.Kind == BindingTypeKind.Handle)
        {
            writer.Write($$"""
                var __result = {{invocation}};
                return __result is null ? 0 : __result.Handle;
            }
            """);
            return writer.ToString();
        }

        writer.Write($$"""
            return {{invocation}};
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedStaticMethod(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var returnTypeName = GetManagedPublicReturnTypeName(function.ReturnType);
        var importMethodName = GetManagedImportMethodName(function);

        if (HasUndefaultableParams(function))
        {
            WriteUndefaultableOverloads(compilation, writer, function, importMethodName, []);
            return writer.ToString();
        }

        if (HasUnexpressibleDefaultParams(function))
        {
            WriteManagedUnexpressibleDefaultConvenienceOverloads(
                writer,
                function,
                signaturePrefix: $"{GetManagedVisibility(function.AccessModifier)} static {returnTypeName} {function.ManagedName}",
                invocationPrefix: string.Empty,
                invocationTarget: function.ManagedName,
                returnTypeName: returnTypeName,
                isConstructor: false);
        }

        var parameterList = RenderManagedPublicParameterList(function.Parameters);

        writer.Write($$"""
            {{GetManagedVisibility(function.AccessModifier)}} static {{returnTypeName}} {{function.ManagedName}}({{parameterList}})
            {
            """);

        if (HasUnexpressibleDefaultParams(function))
        {
            WriteUnexpressibleDefaultCascade(compilation, writer, function, importMethodName, []);
        }
        else
        {
            var invocationPlan = BuildManagedInvocationPlan(compilation, function.Parameters);
            var invokeArguments = string.Join(", ", invocationPlan.Arguments);

            foreach (var statement in invocationPlan.PreCallStatements)
            {
                writer.Write(statement);
            }

            if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.Write($$"""
                    __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }
            }
            else if (function.ReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.Write($$"""
                    var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

                writer.Write($$"""
                    return {{BuildManagedHandleWrap(compilation, function.ReturnType, "__result", function.ReturnsOwnedHandle)}};
                    """);
            }
            else if (function.ReturnType.MarshallerTypeName is not null)
            {
                WriteManagedReturnWithMarshallerConversion(writer, function, importMethodName, invokeArguments, invocationPlan);
            }
            else
            {
                writer.Write($$"""
                    var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

                writer.Write(
                    """
                    return __result;
                    """);
            }
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedStaticImportDeclaration(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var parameterList = string.Join(
            ", ",
            function.Parameters.Select(RenderManagedParameter));
        var returnTypeName = GetManagedImportTypeName(function.ReturnType);
        var returnAttribute = GetManagedReturnAttribute(function.ReturnType);
        var importMethodName = GetManagedImportMethodName(function);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} {importMethodName}({parameterList});";

        writer.Write(
            $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderManagedOperator(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);

        if (function.Parameters.Count == 1 && function.OperatorToken == "==" && !HasOperator(record, "!="))
        {
            writer.Write($$"""
                public bool Equals({{record.ManagedName}} rhs)
                {
                return __PInvoke.{{function.ManagedName}}(this, rhs);
                }
            """);
            return writer.ToString();
        }

        if (function.Parameters.Count == 1 && function.OperatorToken == "!=" && !HasOperator(record, "=="))
        {
            writer.Write($$"""
                public bool Equals({{record.ManagedName}} rhs)
                {
                return !__PInvoke.{{function.ManagedName}}(this, rhs);
                }

                public static bool operator ==({{record.ManagedName}} left, {{record.ManagedName}} right)
                {
                return !__PInvoke.{{function.ManagedName}}(left, right);
                }

                public static bool operator !=({{record.ManagedName}} left, {{record.ManagedName}} right)
                {
                return __PInvoke.{{function.ManagedName}}(left, right);
                }
            """);
            return writer.ToString();
        }

        if (function.Parameters.Count == 1 && function.OperatorToken == "<" && !HasOperator(record, ">"))
        {
            writer.Write($$"""
                public bool LessThan({{record.ManagedName}} rhs)
                {
                return __PInvoke.{{function.ManagedName}}(this, rhs);
                }
            """);
            return writer.ToString();
        }

        if (function.OperatorToken == "!" && function.Parameters.Count == 0)
        {
            var returnTypeName = GetManagedWrapperTypeName(function.ReturnType);
            writer.Write($$"""
                public static {{returnTypeName}} operator !({{record.ManagedName}} value)
                {
                return __PInvoke.{{function.ManagedName}}(value);
                }
                """);
            return writer.ToString();
        }

        if (function.OperatorToken == "!" && function.Parameters.Count == 0)
        {
            var returnTypeName = GetManagedWrapperTypeName(function.ReturnType);
            writer.Write($$"""
                public static {{returnTypeName}} operator !({{record.ManagedName}} value)
                {
                return __PInvoke.{{function.ManagedName}}(value);
                }
                """);
            return writer.ToString();
        }

        if (function.IsConversionOperator)
        {
            var targetTypeName = GetManagedWrapperTypeName(function.ReturnType);
            var conversionKeyword = function.IsExplicitConversionOperator ? "explicit" : "implicit";
            writer.Write($$"""
                public static {{conversionKeyword}} operator {{targetTypeName}}({{record.ManagedName}} value)
                {
                return __PInvoke.{{function.ManagedName}}(value);
                }
                """);
            return writer.ToString();
        }

        if (function.OperatorToken == "+" && function.Parameters.Count == 1)
        {
            var returnTypeName = GetManagedWrapperTypeName(function.ReturnType);
            var parameter = RenderManagedPublicParameter(function.Parameters[0]);
            if (function.ReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.Write($$"""
                    public static {{returnTypeName}} operator +({{record.ManagedName}} left, {{parameter}})
                    {
                    return {{BuildManagedHandleWrap(compilation, function.ReturnType, $"__PInvoke.{function.ManagedName}(left, {RenderManagedSafeHandleInvocationArgument(function.Parameters[0])})", ownsHandle: false)}};
                    }
                    """);
            }
            else
            {
                writer.Write($$"""
                    public static {{returnTypeName}} operator +({{record.ManagedName}} left, {{parameter}})
                    {
                    return __PInvoke.{{function.ManagedName}}(left, {{RenderManagedSafeHandleInvocationArgument(function.Parameters[0])}});
                    }
                    """);
            }
            return writer.ToString();
        }

        if (function.Parameters.Count == 1 && function.OperatorToken is "==" or "!=" or "<" or ">")
        {
            var returnTypeName = GetManagedWrapperTypeName(function.ReturnType);
            var parameterTypeName = GetManagedWrapperTypeName(function.Parameters[0].Type);
            writer.Write($$"""
                public static {{returnTypeName}} operator {{function.OperatorToken}}({{record.ManagedName}} left, {{parameterTypeName}} right)
                {
                return __PInvoke.{{function.ManagedName}}(left, right);
                }
                """);
            return writer.ToString();
        }

        if (function.OperatorToken == "-" && function.Parameters.Count == 0)
        {
            var returnTypeName = GetManagedWrapperTypeName(function.ReturnType);
            if (function.ReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.Write($$"""
                    public static {{returnTypeName}} operator -({{record.ManagedName}} value)
                    {
                    return {{BuildManagedHandleWrap(compilation, function.ReturnType, $"__PInvoke.{function.ManagedName}(value)", ownsHandle: false)}};
                    }
                    """);
            }
            else
            {
                writer.Write($$"""
                    public static {{returnTypeName}} operator -({{record.ManagedName}} value)
                    {
                    return __PInvoke.{{function.ManagedName}}(value);
                    }
                    """);
            }
            return writer.ToString();
        }

        throw new NotSupportedException($"Managed operator emission does not support '{function.ManagedName}'.");
    }

    private static bool HasOperator(RecordBinding record, string operatorToken)
    {
        return record.Members
            .OfType<FunctionBinding>()
            .Any(candidate => string.Equals(candidate.OperatorToken, operatorToken, StringComparison.Ordinal));
    }

    private static string RenderManagedOperatorImportDeclaration(BindingCompilation compilation, FunctionBinding function, string recordManagedName)
    {
        var writer = new CodeWriter();
        var parameterList = function.Parameters.Count == 0
            ? $"{recordManagedName} self"
            : string.Join(", ", new[] { $"{recordManagedName} self" }.Concat(function.Parameters.Select(RenderManagedSafeHandleParameter)));
        var returnTypeName = GetManagedImportTypeName(function.ReturnType);
        var returnAttribute = GetManagedReturnAttribute(function.ReturnType);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} {function.ManagedName}({parameterList});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} {function.ManagedName}({parameterList});";

        writer.Write(
            $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderManagedInstanceImportDeclaration(BindingCompilation compilation, FunctionBinding function, string recordManagedName, bool useSafeHandleParams = false)
    {
        var writer = new CodeWriter();
        Func<ParameterBinding, string> parameterRenderer = useSafeHandleParams ? RenderManagedSafeHandleParameter : RenderManagedParameter;
        var parameterList = string.Join(
            ", ",
            new[] { $"{recordManagedName} self" }.Concat(function.Parameters.Select(parameterRenderer)));
        var returnTypeName = GetManagedImportTypeName(function.ReturnType);
        var returnAttribute = GetManagedReturnAttribute(function.ReturnType);
        var importMethodName = GetManagedImportMethodName(function);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} {importMethodName}({parameterList});";

        writer.Write(
            $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderManagedCallbaseImportDeclaration(BindingCompilation compilation, FunctionBinding function, string recordManagedName)
    {
        var writer = new CodeWriter();
        var parameterList = string.Join(
            ", ",
            new[] { $"{recordManagedName} self" }.Concat(function.Parameters.Select(RenderManagedParameter)));
        var returnTypeName = GetManagedImportTypeName(function.ReturnType);
        var returnAttribute = GetManagedReturnAttribute(function.ReturnType);
        var importMethodName = GetManagedCallbaseImportMethodName(function);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} {importMethodName}({parameterList});";
        var owningRecord = function.FindOwningRecord();
        var entryPointName = owningRecord is not null
            ? GetNativeCallbaseEntrypointName(owningRecord, function)
            : function.EntryPointName;

        writer.Write(
            $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{entryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderInterface(BindingCompilation compilation, RecordBinding record)
    {
        var writer = new CodeWriter();
        var interfaceBaseNames = record.GetManagedInterfaceBaseRecords(compilation)
            .Select(static baseRecord => baseRecord.GetManagedInterfaceName())
            .ToArray();
        var baseClause = interfaceBaseNames.Length == 0
            ? string.Empty
            : $" : {string.Join(", ", interfaceBaseNames)}";

        writer.Write($$"""
            public partial interface {{record.GetManagedInterfaceName()}}{{baseClause}}
            {
            """);

        foreach (var method in record.GetRenderableInterfaceMethods(compilation).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            writer.WriteLine();
            var parameterList = RenderManagedPublicParameterList(method.Parameters);
            var returnTypeName = GetManagedPublicReturnTypeName(method.ReturnType);
            WriteDocumentationComment(writer, method);
            writer.Write($$"""
                {{returnTypeName}} {{method.ManagedName}}({{parameterList}});
                """);
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderEnum(EnumBinding @enum)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, @enum);

        if (@enum.IsFlags)
        {
            writer.Write("[Flags]");
        }

        writer.Write($$"""
            public enum {{@enum.ManagedName}} : {{GetManagedEnumUnderlyingTypeName(@enum)}}
            {
            """);

        foreach (var value in @enum.Values)
        {
            writer.Write($$"""
                {{value.ManagedName}} = {{value.ValueExpression}},
                """);
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedProperty(BindingCompilation compilation, FieldBinding field)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, field);
        var propertyTypeName = GetManagedPublicReturnTypeName(field.Type);
        writer.Write($$"""
            {{GetManagedVisibility(field.AccessModifier)}} {{propertyTypeName}} {{field.ManagedName}}
            {
            get
            {
            """);

        if (field.Type.Kind == BindingTypeKind.Handle)
        {
            writer.Write($$"""
                return {{BuildManagedHandleWrap(compilation, field.Type, $"__PInvoke.Get_{field.ManagedName}(this)", field.ReturnsOwnedHandle)}};
                """);
        }
        else
        {
            writer.Write($$"""
                return __PInvoke.Get_{{field.ManagedName}}(this);
                """);
        }

        writer.Write(
            """
            }
            """);

        if (!field.IsReadOnly)
        {
            var setterArgument = RequiresHandleWrapping(field.Type)
                ? "global::SharpCpp.Support.NativeObject.GetHandle(value)"
                : "value";
            var setterKeepAlive = RequiresHandleWrapping(field.Type)
                ? "\n            global::System.GC.KeepAlive(value);"
                : string.Empty;
            writer.Write($$"""
                set
                {
                __PInvoke.Set_{{field.ManagedName}}(this, {{setterArgument}});{{setterKeepAlive}}
                }
                """);
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedFieldGetterImportDeclaration(BindingCompilation compilation, FieldBinding field, string recordManagedName)
    {
        var writer = new CodeWriter();
        var returnTypeName = GetManagedImportTypeName(field.Type);
        var returnAttribute = GetManagedReturnAttribute(field.Type);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} Get_{field.ManagedName}({recordManagedName} self);"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} Get_{field.ManagedName}({recordManagedName} self);";

        writer.Write($$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "sharpcpp_e2e_{{SanitizeNativeEntrypointName(field.OwningRecordCppTypeName)}}_{{field.NativeName}}_get", StringMarshalling = StringMarshalling.Utf8)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderManagedFieldSetterImportDeclaration(BindingCompilation compilation, FieldBinding field, string recordManagedName)
    {
        var writer = new CodeWriter();
        var parameter = RenderManagedParameter(
            new ParameterBinding(
                $"field:set:{field.NativeName}",
                "value",
                "value",
                field.Type,
                BindingParameterDirection.In,
                BindingParameterAbiKind.Value,
                field.CppTypeName));

        writer.Write($$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "sharpcpp_e2e_{{SanitizeNativeEntrypointName(field.OwningRecordCppTypeName)}}_{{field.NativeName}}_set", StringMarshalling = StringMarshalling.Utf8)]
            internal static partial void Set_{{field.ManagedName}}({{recordManagedName}} self, {{parameter}});
            """);
        return writer.ToString();
    }

    private static string RenderNativeBindings(BindingCompilation compilation,
        Func<BindingNode, bool>? memberFilter = null, string? groupHeaderPath = null)
    {
        var writer = new CodeWriter();
        var allFunctions = compilation.Functions.AsEnumerable();
        var allRecords = compilation.Records.Where(static record => !record.GetMetadata<bool>(MetadataKeys.SuppressEmission)).AsEnumerable();

        if (memberFilter is not null)
        {
            allFunctions = allFunctions.Where(f => memberFilter(f));
            allRecords = allRecords.Where(r => memberFilter(r));
        }

        var functions = allFunctions.OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
        var records = allRecords.OrderBy(static record => record.ManagedName, StringComparer.Ordinal).ToArray();

        writer.Write(
            """
            // <auto-generated />
            #include "SharpCpp/Export.hpp"
            #include "SharpCpp/Marshaller.hpp"
            #include "SharpCpp/detail/MarshallerSupport.hpp"
            #include <stdexcept>
            #include <typeinfo>

            using namespace SharpCpp;
            """);

        foreach (var headerFile in compilation.HeaderFiles.OrderBy(static header => header, StringComparer.Ordinal))
        {
            writer.Write($$"""

                #include "{{headerFile}}"
                """);
        }

        foreach (var headerFile in compilation.NativeSupportHeaders.OrderBy(static header => header, StringComparer.Ordinal))
        {
            writer.Write($$"""

                #include "{{headerFile}}"
                """);
        }

        foreach (var record in records.Where(record => record.RequiresDirector(compilation)).OrderBy(static record => record.ManagedName, StringComparer.Ordinal))
        {
            writer.WriteLine();
            writer.Write(RenderNativeDirectorClass(compilation, record));
        }

        var directorRecords = records.Where(record => record.RequiresDirector(compilation)).ToArray();
        foreach (var record in directorRecords)
        {
            var callbaseMethods = record.GetEffectiveVirtualMethodSlots(compilation)
                .Select(static slot => slot.Method)
                .Where(static method => !method.IsPureVirtual)
                .ToArray();
            foreach (var method in callbaseMethods)
            {
                var parameterList = string.Join(", ", new[] { $"arg_t<{record.CppQualifiedName}*> self" }.Concat(method.Parameters.Select(RenderNativeParameter)));
                var abiArguments = string.Join(", ", method.Parameters.Select(static p => p.NativeName));
                var parameterCppList = string.Join(", ", method.Parameters.Select(static p => p.CppTypeName));
                var callbaseArguments = string.Join(", ", method.Parameters.Select(static p => p.NativeName));
                var directorName = GetNativeDirectorClassName(record);
                var helperName = GetNativeCallbaseHelperName(method);

                writer.WriteLine();
                if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
                {
                    writer.Write($$"""
                        SHARPCPP_EXPORT ret_t<void> {{GetNativeCallbaseEntrypointName(record, method)}}({{parameterList}})
                        {
                        using FT = void({{record.CppQualifiedName}}*{{(parameterCppList.Length > 0 ? $", {parameterCppList}" : string.Empty)}});
                        MarshallerInvoker::invoke_as<FT>(&{{directorName}}::{{helperName}}, self{{(callbaseArguments.Length > 0 ? $", {callbaseArguments}" : string.Empty)}});
                        }
                        """);
                }
                else
                {
                    writer.Write($$"""
                        SHARPCPP_EXPORT ret_t<{{method.CppReturnTypeName}}> {{GetNativeCallbaseEntrypointName(record, method)}}({{parameterList}})
                        {
                        using FT = {{method.CppReturnTypeName}}({{record.CppQualifiedName}}*{{(parameterCppList.Length > 0 ? $", {parameterCppList}" : string.Empty)}});
                        return MarshallerInvoker::invoke_as<FT>(&{{directorName}}::{{helperName}}, self{{(callbaseArguments.Length > 0 ? $", {callbaseArguments}" : string.Empty)}});
                        }
                        """);
                }

                if (HasUnexpressibleDefaultParams(method) || HasUndefaultableParams(method))
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeUnexpressibleDefaultCallbase(record, method));
                }
            }
        }

        foreach (var record in records.Where(static record => record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static record => record.ManagedName, StringComparer.Ordinal))
        {
            foreach (var instantiation in record.SupportedGenericInstantiations)
            {
                foreach (var constructor in record.Members.OfType<FunctionBinding>().Where(static function => function.IsConstructor && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.Parameters.Count))
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeGenericConstructor(record, constructor, instantiation));
                }

                foreach (var method in record.Members.OfType<FunctionBinding>().Where(static function => !function.IsConstructor && !function.IsStatic && !function.IsGeneric && function.OperatorToken is null && !function.IsConversionOperator && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.ManagedName, StringComparer.Ordinal))
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeGenericInstanceMethod(record, method, instantiation));
                }

                foreach (var method in record.Members.OfType<FunctionBinding>().Where(static function => function.IsStatic && !function.IsGeneric && function.OperatorToken is null && !function.IsConversionOperator && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.ManagedName, StringComparer.Ordinal))
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeGenericStaticMethod(record, method, instantiation));
                }

                if (CanEmitClone(record, isEffectivelyAbstract: false, canEmitDeleteThunk: true))
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeGenericClone(record, instantiation));
                }

                writer.WriteLine();
                writer.Write(RenderNativeGenericDelete(record, instantiation));
            }

            var genericInstanceMethods = record.Members.OfType<FunctionBinding>()
                .Where(static function => !function.IsConstructor && !function.IsStatic && function.IsGeneric && function.OperatorToken is null && !function.IsConversionOperator && !function.HasMetadata(MetadataKeys.IgnoreReason))
                .OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
            foreach (var method in genericInstanceMethods)
            {
                var methodInstantiations = method.SupportedGenericInstantiations.ToArray();
                if (methodInstantiations.Length == 0)
                {
                    continue;
                }

                foreach (var instantiation in record.SupportedGenericInstantiations)
                {
                    foreach (var methodInstantiation in methodInstantiations)
                    {
                        writer.WriteLine();
                        writer.Write(RenderNativeGenericMethodInstanceMethod(record, method, instantiation, methodInstantiation));
                    }
                }
            }
        }

        foreach (var function in functions)
        {
            if (function.Parent is RecordBinding || function.IsConstructor || function.HasMetadata(MetadataKeys.IgnoreReason))
            {
                continue;
            }

            if (function.IsGeneric)
            {
                foreach (var instantiation in function.SupportedGenericInstantiations)
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeGenericFreeFunction(function, instantiation));
                }

                continue;
            }

            var parameterList = string.Join(", ", function.Parameters.Select(RenderNativeParameter));
            var abiArguments = string.Join(", ", function.Parameters.Select(static p => p.NativeName));
            var signature = BuildNativeFunctionSignature(function);

            writer.WriteLine();
            writer.Write($$"""
                SHARPCPP_EXPORT ret_t<{{function.CppReturnTypeName}}> {{function.EntryPointName}}({{parameterList}})
                {
                using FT = {{signature}};
                """);

            if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.Write($$"""
                    MarshallerInvoker::invoke_as<FT>(static_cast<std::add_pointer_t<FT>>(&{{function.CppQualifiedName}}){{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)}});
                    """);
            }
            else
            {
                writer.Write($$"""
                    return MarshallerInvoker::invoke_as<FT>(static_cast<std::add_pointer_t<FT>>(&{{function.CppQualifiedName}}){{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)}});
                    """);
            }

            writer.Write("""
                }
                """);

            if (HasUnexpressibleDefaultParams(function) || HasUndefaultableParams(function))
            {
                writer.WriteLine();
                writer.Write(RenderNativeUnexpressibleDefaultFreeFunction(function));
            }
        }

        foreach (var method in records.Where(static record => !record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason)).SelectMany(static record => record.Members.OfType<FunctionBinding>()).Where(static function => !function.IsConstructor && !function.IsStatic && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            var parameterList = string.Join(", ", new[] { $"arg_t<{method.OwningRecordCppTypeName}*> self" }.Concat(method.Parameters.Select(RenderNativeParameter)));
            var abiArguments = string.Join(", ", method.Parameters.Select(static p => p.NativeName));
            var memberSignature = BuildNativeMethodSignature(method);

            writer.WriteLine();
            if (method.TryGetMetadata(MetadataKeys.MarshallerExposedMethod, out string? marshallerCppType))
            {
                writer.Write($$"""
                    SHARPCPP_EXPORT ret_t<{{method.CppReturnTypeName}}> {{method.EntryPointName}}({{parameterList}})
                    {
                    {{RenderNativeMarshallerExposedInvocation(method, marshallerCppType!, abiArguments)}}
                    }
                    """);
            }
            else
            {
            writer.Write($$"""
                SHARPCPP_EXPORT ret_t<{{method.CppReturnTypeName}}> {{method.EntryPointName}}({{parameterList}})
                {
                using FT = {{memberSignature}};
                """);

            var owningRecord = method.Parent as RecordBinding;
            writer.Write(method.AccessModifier == BindingAccessModifier.Protected && owningRecord is not null && owningRecord.RequiresDirector(compilation)
                ? RenderNativeProtectedMethodInvocation(compilation, method, abiArguments, memberSignature)
                : RenderNativeMethodInvocation(method, abiArguments, memberSignature));

            writer.Write("""
                }
                """);
            }

            if (HasUnexpressibleDefaultParams(method) || HasUndefaultableParams(method))
            {
                writer.WriteLine();
                writer.Write(RenderNativeUnexpressibleDefaultInstanceMethod(method));
            }
        }

        foreach (var method in records.Where(static record => !record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason)).SelectMany(static record => record.Members.OfType<FunctionBinding>()).Where(static function => function.IsStatic && !function.HasMetadata(MetadataKeys.IgnoreReason) && function.AccessModifier != BindingAccessModifier.Protected).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            var parameterList = string.Join(", ", method.Parameters.Select(RenderNativeParameter));
            var abiArguments = string.Join(", ", method.Parameters.Select(static p => p.NativeName));
            var signature = BuildNativeFunctionSignature(method);

            writer.WriteLine();
            writer.Write($$"""
                SHARPCPP_EXPORT ret_t<{{method.CppReturnTypeName}}> {{method.EntryPointName}}({{parameterList}})
                {
                using FT = {{signature}};
                """);

            if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.Write($$"""
                    MarshallerInvoker::invoke_as<FT>(&{{method.OwningRecordCppTypeName}}::{{method.NativeName}}{{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)}});
                    """);
            }
            else
            {
                writer.Write($$"""
                    return MarshallerInvoker::invoke_as<FT>(&{{method.OwningRecordCppTypeName}}::{{method.NativeName}}{{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)}});
                    """);
            }

            writer.Write("""
                }
                """);

            if (HasUnexpressibleDefaultParams(method) || HasUndefaultableParams(method))
            {
                writer.WriteLine();
                writer.Write(RenderNativeUnexpressibleDefaultStaticMethod(method));
            }
        }

        foreach (var constructor in records.Where(static record => !record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason)).SelectMany(static record => record.Members.OfType<FunctionBinding>()).Where(static function => function.IsConstructor && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.ManagedName, StringComparer.Ordinal))
        {
            var parameterList = string.Join(", ", constructor.Parameters.Select(RenderNativeParameter));
            var abiArguments = string.Join(", ", constructor.Parameters.Select(static p => p.NativeName));
            var constructorArgTypes = string.Join(", ", constructor.Parameters.Select(static p => p.CppTypeName));
            var owningRecord = constructor.Parent as RecordBinding;

            writer.WriteLine();
            writer.Write($$"""
                SHARPCPP_EXPORT ret_t<{{constructor.CppReturnTypeName}}> {{constructor.EntryPointName}}({{parameterList}})
                {
                {{(owningRecord is not null && owningRecord.RequiresDirector(compilation)
                    ? $"return new {GetNativeDirectorClassName(owningRecord)}({abiArguments});"
                    : $"return MarshallerInvoker::construct<{constructor.OwningRecordCppTypeName}{(constructorArgTypes.Length > 0 ? $", {constructorArgTypes}" : string.Empty)}>({abiArguments});")}}
                }
                """);

            if (HasUnexpressibleDefaultParams(constructor) || HasUndefaultableParams(constructor))
            {
                writer.WriteLine();
                writer.Write(RenderNativeUnexpressibleDefaultConstructor(constructor, owningRecord is not null && owningRecord.RequiresDirector(compilation)));
            }
        }

        foreach (var field in records.Where(static record => !record.HasMetadata(MetadataKeys.IgnoreReason)).SelectMany(static record => record.Members.OfType<FieldBinding>()).Where(static field => !field.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static field => field.ManagedName, StringComparer.Ordinal))
        {
            var nativeFieldCppType = GetNativeFieldCppType(compilation, field);
            var borrowFieldStorage = ShouldBorrowFieldStorage(compilation, field);
            writer.WriteLine();
            writer.Write($$"""
                SHARPCPP_EXPORT ret_t<{{nativeFieldCppType}}> sharpcpp_e2e_{{SanitizeNativeEntrypointName(field.OwningRecordCppTypeName)}}_{{field.NativeName}}_get(arg_t<{{field.OwningRecordCppTypeName}}*> self)
                {
                {{(field.AccessModifier == BindingAccessModifier.Protected
                    ? RenderNativeProtectedFieldGetInvocation(compilation, field)
                    : borrowFieldStorage
                        ? RenderNativeBorrowedFieldGetInvocation(field)
                        : $"return MarshallerInvoker::get_field<{nativeFieldCppType}>(&{field.OwningRecordCppTypeName}::{field.NativeName}, self);")}}
                }
                """);

            if (!field.IsReadOnly)
            {
                writer.WriteLine();
                writer.Write($$"""
                    SHARPCPP_EXPORT ret_t<void> sharpcpp_e2e_{{SanitizeNativeEntrypointName(field.OwningRecordCppTypeName)}}_{{field.NativeName}}_set(arg_t<{{field.OwningRecordCppTypeName}}*> self, arg_t<{{nativeFieldCppType}}> value)
                    {
                    {{(field.AccessModifier == BindingAccessModifier.Protected
                        ? RenderNativeProtectedFieldSetInvocation(compilation, field)
                        : borrowFieldStorage
                            ? RenderNativeBorrowedFieldSetInvocation(field)
                            : $"MarshallerInvoker::set_field<{nativeFieldCppType}>(&{field.OwningRecordCppTypeName}::{field.NativeName}, self, value);")}}
                    }
                    """);
            }
        }

        foreach (var record in records.Where(static record => !record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static record => record.ManagedName, StringComparer.Ordinal))
        {
            writer.WriteLine();
            if (!record.GetMetadata<bool>(MetadataKeys.NonPublicDestructor))
            {
                writer.Write(RenderNativeDelete(record, record.RequiresDirector(compilation)));
            }

            if (CanEmitClone(record, record.IsEffectivelyAbstract || record.HasUnimplementedAbstractBaseMethods(compilation), !record.GetMetadata<bool>(MetadataKeys.NonPublicDestructor)))
            {
                writer.WriteLine();
                writer.Write(RenderNativeClone(record));
            }

            if (record.RequiresDirector(compilation))
            {
                writer.WriteLine();
                writer.Write(RenderNativeConnectDirector(record, record.GetEffectiveVirtualMethodSlots(compilation)));
            }

            if (!record.IsPolymorphic)
            {
                continue;
            }

            writer.WriteLine();
            writer.Write($$"""
                SHARPCPP_EXPORT ret_t<std::size_t> sharpcpp_e2e_{{SanitizeNativeEntrypointName(record.NativeName)}}_type_id()
                {
                return typeid({{record.CppQualifiedName}}).hash_code();
                }
                """);

            writer.WriteLine();
            writer.Write($$"""
                SHARPCPP_EXPORT ret_t<void> sharpcpp_e2e_{{SanitizeNativeEntrypointName(record.NativeName)}}_metadata(arg_t<{{record.CppQualifiedName}}*> self, void** handle, std::size_t* dynamicTypeId)
                {
                if (handle == nullptr || dynamicTypeId == nullptr)
                {
                return;
                }

                if (self == nullptr)
                {
                *handle = nullptr;
                *dynamicTypeId = 0;
                return;
                }

                *handle = dynamic_cast<void*>(self);
                *dynamicTypeId = typeid(*self).hash_code();
                }
                """);
        }

        return writer.ToString();
    }

    private static string GetManagedWrapperTypeName(BindingType type)
    {
        return type.Kind switch
        {
            BindingTypeKind.Primitive => type.ManagedTypeName,
            BindingTypeKind.Handle => type.ManagedTypeName,
            BindingTypeKind.Enum => NormalizeEnumManagedTypeName(type.ManagedTypeName),
            BindingTypeKind.GenericParameter => type.ManagedTypeName,
            _ => throw new NotSupportedException($"Managed wrapper emission does not support '{type.Kind}'.")
        };
    }

    private static string GetManagedPublicReturnTypeName(BindingType type)
    {
        return GetManagedWrapperTypeName(type);
    }

    /// <summary>
    /// Writes the P/Invoke result variable, post-call statements, and conversion
    /// for types where import type differs from public type (e.g., IntPtr → string).
    /// This path is used when a marshaller_type is set but NO managed_marshaller.
    /// New types should use managed_marshaller (C# custom marshaller) instead.
    /// </summary>
    private static void WriteManagedReturnWithMarshallerConversion(
        CodeWriter writer,
        FunctionBinding function,
        string importMethodName,
        string invokeArguments,
        ManagedInvocationPlan invocationPlan)
    {
        var importTypeName = GetManagedImportTypeName(function.ReturnType);
        var publicTypeName = function.ReturnType.ManagedTypeName;
        writer.Write($$"""
            var __result_raw = __PInvoke.{{importMethodName}}({{invokeArguments}});
            """);

        foreach (var statement in invocationPlan.PostCallStatements)
        {
            writer.Write(statement);
        }

        var conversionExpression = importTypeName == "global::System.IntPtr" && publicTypeName == "string"
            ? "global::System.Runtime.InteropServices.Marshal.PtrToStringUTF8(__result_raw)!"
            : "__result_raw";
        writer.Write($$"""
            return {{conversionExpression}};
            """);
    }

    private static string GetManagedImportTypeName(BindingType type)
    {
        if (type.MarshallerTypeName is not null)
        {
            return type.MarshallerTypeName;
        }

        return type.Kind switch
        {
            BindingTypeKind.Primitive => type.ManagedTypeName,
            BindingTypeKind.Handle => "nint",
            BindingTypeKind.Enum => NormalizeEnumManagedTypeName(type.ManagedTypeName),
            BindingTypeKind.GenericParameter => type.ManagedTypeName,
            _ => throw new NotSupportedException($"Managed import emission does not support '{type.Kind}'.")
        };
    }

    private static string RenderManagedSafeHandleParameter(ParameterBinding parameter)
    {
        var modifier = parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => "in ",
            BindingParameterDirection.Ref => "ref ",
            BindingParameterDirection.Out => "out ",
            _ => string.Empty
        };
        var attribute = GetManagedParameterAttribute(parameter.Type);
        var typeName = parameter.Type.Kind == BindingTypeKind.Handle
            ? $"{GetManagedWrapperTypeName(parameter.Type)}?"
            : GetManagedWrapperTypeName(parameter.Type);

        return $"{attribute}{modifier}{typeName} {parameter.ManagedName}";
    }

    private static string RenderManagedParameter(ParameterBinding parameter)
    {
        var modifier = parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => "in ",
            BindingParameterDirection.Ref => "ref ",
            BindingParameterDirection.Out => "out ",
            _ => string.Empty
        };
        var attribute = GetManagedParameterAttribute(parameter.Type);

        return $"{attribute}{modifier}{GetManagedImportTypeName(parameter.Type)} {parameter.ManagedName}";
    }

    private static string RenderManagedPublicParameter(ParameterBinding parameter)
    {
        return RenderManagedPublicParameterCore(parameter, forceRequired: false, suppressDefault: false);
    }

    private static string RenderManagedPublicParameterList(IEnumerable<ParameterBinding> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var parameterArray = parameters as ParameterBinding[] ?? parameters.ToArray();

        var firstUnexpressibleDefault = parameterArray
            .Select((parameter, index) => (parameter, index))
            .FirstOrDefault(static entry => entry.parameter.HasMetadata(MetadataKeys.UnexpressibleDefault));

        var parts = new List<string>(parameterArray.Length);
        for (var i = 0; i < parameterArray.Length; i++)
        {
            var parameter = parameterArray[i];
            if (firstUnexpressibleDefault.parameter is not null)
            {
                if (i < firstUnexpressibleDefault.index)
                {
                    parts.Add(RenderManagedPublicParameterRequired(parameter));
                    continue;
                }

                if (i == firstUnexpressibleDefault.index)
                {
                    parts.Add(RenderManagedPublicParameterNoDefault(parameter));
                    continue;
                }
            }

            parts.Add(RenderManagedPublicParameter(parameter));
        }

        return string.Join(", ", parts);
    }

    private static void WriteManagedUnexpressibleDefaultConvenienceOverloads(
        CodeWriter writer,
        FunctionBinding function,
        string signaturePrefix,
        string invocationPrefix,
        string invocationTarget,
        string? returnTypeName,
        bool isConstructor)
    {
        foreach (var keepCount in GetVariantParameterIndices(function).Where(keepCount => keepCount < function.Parameters.Count))
        {
            var variantParams = function.Parameters.Take(keepCount).ToArray();
            var parameterList = RenderManagedPublicParameterList(variantParams);
            var forwardedArgs = string.Join(
                ", ",
                variantParams.Select(static parameter => parameter.ManagedName)
                    .Concat(function.Parameters.Skip(keepCount).Select(GetManagedOmittedDefaultArgument)));
            var overloadSignaturePrefix = signaturePrefix;

            if (!isConstructor &&
                string.Equals(invocationTarget, nameof(ToString), StringComparison.Ordinal) &&
                variantParams.Length == 0 &&
                returnTypeName is not null &&
                !overloadSignaturePrefix.Contains(" new ", StringComparison.Ordinal))
            {
                overloadSignaturePrefix = overloadSignaturePrefix.Replace(
                    $"{returnTypeName} {invocationTarget}",
                    $"new {returnTypeName} {invocationTarget}",
                    StringComparison.Ordinal);
            }

            if (isConstructor)
            {
                writer.Write($$"""
                    {{signaturePrefix}}({{parameterList}}) {{invocationPrefix}}({{forwardedArgs}})
                    {
                    }

                    """);
                continue;
            }

            writer.Write($$"""
                {{overloadSignaturePrefix}}({{parameterList}})
                {
                """);

            if (string.Equals(returnTypeName, "void", StringComparison.Ordinal))
            {
                writer.Write($$"""
                    {{invocationTarget}}({{forwardedArgs}});
                    }

                    """);
            }
            else
            {
                writer.Write($$"""
                    return {{invocationTarget}}({{forwardedArgs}});
                    }

                    """);
            }
        }
    }

    private static string GetManagedOmittedDefaultArgument(ParameterBinding parameter)
    {
        if (parameter.HasMetadata(MetadataKeys.UnexpressibleDefault) || parameter.ManagedDefaultValueExpression == "null")
        {
            return $"default({GetManagedOmittedDefaultTypeName(parameter)})";
        }

        if (parameter.ManagedDefaultValueExpression is { Length: > 0 } defaultValueExpression)
        {
            return RenderManagedDefaultValueExpression(parameter, defaultValueExpression);
        }

        return "default";
    }

    private static string GetManagedOmittedDefaultTypeName(ParameterBinding parameter)
    {
        var baseTypeName = GetManagedWrapperTypeName(parameter.Type);
        if (parameter.Type.IsPointerType)
        {
            return $"{baseTypeName}?";
        }

        return parameter.HasMetadata(MetadataKeys.UnexpressibleDefault)
            ? $"{baseTypeName}?"
            : baseTypeName;
    }

    private static string RenderManagedPublicParameterRequired(ParameterBinding parameter)
    {
        return RenderManagedPublicParameterCore(parameter, forceRequired: true, suppressDefault: true);
    }

    private static string RenderManagedPublicParameterNoDefault(ParameterBinding parameter)
    {
        return RenderManagedPublicParameterCore(parameter, forceRequired: false, suppressDefault: true);
    }

    private static string RenderManagedPublicParameterCore(ParameterBinding parameter, bool forceRequired, bool suppressDefault)
    {
        var modifier = parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => "in ",
            BindingParameterDirection.Ref => "ref ",
            BindingParameterDirection.Out => "out ",
            _ => string.Empty
        };

        var isNullableHandleDefault = !forceRequired &&
            parameter.Direction == BindingParameterDirection.In &&
            parameter.AbiKind == BindingParameterAbiKind.Value &&
            parameter.ManagedDefaultValueExpression == "null" &&
            parameter.Type.IsPointerType;

        var typeName = (!forceRequired && parameter.HasMetadata(MetadataKeys.UnexpressibleDefault)) || isNullableHandleDefault
            ? $"{GetManagedWrapperTypeName(parameter.Type)}?"
            : GetManagedWrapperTypeName(parameter.Type);

        var signature = $"{modifier}{typeName} {parameter.ManagedName}";

        if (!forceRequired && !suppressDefault)
        {
            if (parameter.HasMetadata(MetadataKeys.UnexpressibleDefault))
            {
            }
            else if (isNullableHandleDefault)
            {
                signature += " = default";
            }
            else if (parameter.Direction == BindingParameterDirection.In &&
                parameter.AbiKind == BindingParameterAbiKind.Value &&
                parameter.ManagedDefaultValueExpression is { Length: > 0 } defaultValueExpression)
            {
                var fallbackDefault = defaultValueExpression == "null"
                    ? "default"
                    : RenderManagedDefaultValueExpression(parameter, defaultValueExpression);
                signature += $" = {fallbackDefault}";
            }
        }
        return signature;
    }

    private static string RenderManagedDefaultValueExpression(ParameterBinding parameter, string defaultValueExpression)
    {
        if (defaultValueExpression.Length >= 3 &&
            defaultValueExpression[0] == '\'' &&
            defaultValueExpression[^1] == '\'')
        {
            return parameter.Type.PrimitiveKind switch
            {
                BindingPrimitiveKind.Int8 => $"(sbyte){defaultValueExpression}",
                BindingPrimitiveKind.UInt8 => $"(byte){defaultValueExpression}",
                _ => defaultValueExpression
            };
        }

        return defaultValueExpression;
    }

    private static string RenderNativeParameter(ParameterBinding parameter)
    {
        return $"arg_t<{parameter.CppTypeName}> {parameter.NativeName}";
    }

    private static string BuildNativeFunctionSignature(FunctionBinding function)
    {
        var returnTypeName = function.CppReturnTypeName;
        var parameterTypes = string.Join(", ", function.Parameters.Select(static p => p.CppTypeName));
        return $"{returnTypeName}({parameterTypes})";
    }

    private static string BuildNativeMethodSignature(FunctionBinding method)
    {
        var returnTypeName = method.CppReturnTypeName;
        var parameterTypes = string.Join(", ", method.Parameters.Select(static p => p.CppTypeName));
        var constQualifier = method.IsConstMethod ? " const" : string.Empty;
        return $"{returnTypeName}({method.OwningRecordCppTypeName}::*)({parameterTypes}){constQualifier}";
    }

    private static string GetNativeFieldCppType(BindingCompilation compilation, FieldBinding field)
    {
        if (field.Type.Kind != BindingTypeKind.Handle)
        {
            return field.CppTypeName;
        }

        if (field.CppTypeName.Contains('*', StringComparison.Ordinal) || field.CppTypeName.Contains('&', StringComparison.Ordinal))
        {
            return field.CppTypeName;
        }

        var record = ResolveRecord(compilation, field.Type);
        if (record is not null &&
            record.Members.OfType<FunctionBinding>().Any(static method => method.HasMetadata(MetadataKeys.MarshallerExposedMethod)) &&
            field.Type.CppTypeName.EndsWith("*", StringComparison.Ordinal))
        {
            return field.Type.CppTypeName[..^1];
        }

        return field.CppTypeName;
    }


    private static bool RequiresHandleWrapping(BindingType type)
    {
        return type.IsPointerType;
    }

    private static string RenderManagedInvocationArgument(ParameterBinding parameter)
    {
        var requiresWrapping = RequiresHandleWrapping(parameter.Type);
        var valueExpression = requiresWrapping
            ? $"global::SharpCpp.Support.NativeObject.GetHandle({parameter.ManagedName})"
            : parameter.ManagedName;

        if (!requiresWrapping)
        {
            return parameter.Direction switch
            {
                BindingParameterDirection.Ref => $"ref {valueExpression}",
                BindingParameterDirection.Out => $"out {valueExpression}",
                _ => valueExpression
            };
        }

        return parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => $"in {valueExpression}",
            BindingParameterDirection.Ref => $"ref {valueExpression}",
            BindingParameterDirection.Out => $"out {valueExpression}",
            _ => valueExpression
        };
    }

    private static string RenderManagedSafeHandleInvocationArgument(ParameterBinding parameter)
    {
        return parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => $"in {parameter.ManagedName}",
            BindingParameterDirection.Ref => $"ref {parameter.ManagedName}",
            BindingParameterDirection.Out => $"out {parameter.ManagedName}",
            _ => parameter.ManagedName
        };
    }

    private static ManagedInvocationPlan BuildManagedInvocationPlan(BindingCompilation compilation, IEnumerable<ParameterBinding> parameters)
    {
        var arguments = new List<string>();
        var preCallStatements = new List<string>();
        var postCallStatements = new List<string>();

        foreach (var parameter in parameters)
        {
            var requiresWrapping = RequiresHandleWrapping(parameter.Type);

            if (requiresWrapping && parameter.Direction is BindingParameterDirection.Ref or BindingParameterDirection.Out)
            {
                var abiVariableName = $"__abi_{parameter.ManagedName}";

                if (parameter.Direction == BindingParameterDirection.Ref)
                {
                    preCallStatements.Add($$"""
                        var {{abiVariableName}} = global::SharpCpp.Support.NativeObject.GetHandle({{parameter.ManagedName}});
                        """);
                    arguments.Add($"ref {abiVariableName}");
                }
                else
                {
                    preCallStatements.Add($$"""
                        nint {{abiVariableName}};
                        """);
                    arguments.Add($"out {abiVariableName}");
                }

                postCallStatements.Add($$"""
                    {{parameter.ManagedName}} = {{BuildManagedHandleWrap(compilation, parameter.Type, abiVariableName, ownsHandle: false)}};
                    """);
                continue;
            }

            if (requiresWrapping)
            {
                postCallStatements.Add($"global::System.GC.KeepAlive({parameter.ManagedName});");
            }

            arguments.Add(RenderManagedInvocationArgument(parameter));
        }

        return new ManagedInvocationPlan(arguments, preCallStatements, postCallStatements);
    }

    private sealed record ManagedInvocationPlan(
        IReadOnlyList<string> Arguments,
        IReadOnlyList<string> PreCallStatements,
        IReadOnlyList<string> PostCallStatements);

    private sealed record AccessorProperty(string PropertyName, FunctionBinding? Getter, FunctionBinding? Setter);

    private static string GetManagedInstanceMethodModifier(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        var hasManagedBase = !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName);
        var hidingObjectToString = string.Equals(function.ManagedName, nameof(ToString), StringComparison.Ordinal) &&
            function.Parameters.Count == 0 &&
            !function.IsOverride;
        var hidingModifier = hidingObjectToString ? "new " : string.Empty;
        var primaryBaseVirtualMatch = hasManagedBase ? FindPrimaryBaseVirtualMatch(compilation, record, function) : null;

        if (function.IsPureVirtual && primaryBaseVirtualMatch is null)
        {
            return hidingModifier + "abstract ";
        }

        if (function.IsVirtual)
        {
            var modifier = primaryBaseVirtualMatch is null
                ? (function.IsOverride && !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) &&
                   record.ManagedBaseTypeName.Contains('.', StringComparison.Ordinal) ? "override " : "virtual ")
                : "override ";

            if (function.IsFinal && modifier == "override ")
            {
                modifier += "sealed ";
            }

            return hidingModifier + modifier;
        }

        if (function.IsOverride && !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) &&
            record.ManagedBaseTypeName.Contains('.', StringComparison.Ordinal))
        {
            return "override ";
        }

        return hidingModifier.Length > 0 || ShouldUseNewModifier(compilation, record, function.ManagedName) ? "new " : string.Empty;
    }

    private static string GetManagedVisibility(BindingAccessModifier accessModifier)
    {
        return accessModifier == BindingAccessModifier.Protected ? "protected" : "public";
    }

    private static string GetManagedVisibilityNormalized(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        if (!function.IsVirtual)
        {
            return GetManagedVisibility(function.AccessModifier);
        }

        var mostRestrictiveBaseAccess = FindMostRestrictiveBaseAccess(compilation, record, function);
        if (mostRestrictiveBaseAccess == BindingAccessModifier.Protected)
        {
            return "protected";
        }

        return "public";
    }

    private static BindingAccessModifier FindMostRestrictiveBaseAccess(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        var foundBaseMatch = false;
        var mostRestrictive = BindingAccessModifier.Public;

        foreach (var @base in record.Bases)
        {
            var baseRecord = compilation.Records.FirstOrDefault(candidate => string.Equals(candidate.SymbolId, @base.RecordId, StringComparison.Ordinal));
            if (baseRecord is null)
            {
                continue;
            }

            var match = baseRecord.Members
                .OfType<FunctionBinding>()
                .FirstOrDefault(bm => !bm.IsIgnored() && bm.IsVirtual && bm.ManagedName == function.ManagedName && bm.Parameters.Count == function.Parameters.Count);

            if (match is not null)
            {
                foundBaseMatch = true;
                if (match.AccessModifier == BindingAccessModifier.Protected)
                {
                    mostRestrictive = BindingAccessModifier.Protected;
                }
                else
                {
                    var inheritedRestrictive = FindMostRestrictiveBaseAccess(compilation, baseRecord, match);
                    if (inheritedRestrictive == BindingAccessModifier.Protected)
                    {
                        mostRestrictive = BindingAccessModifier.Protected;
                    }
                }
            }
        }

        return foundBaseMatch ? mostRestrictive : function.AccessModifier;
    }

    private static BindingAccessModifier GetAccessorPropertyAccessModifier(AccessorProperty property)
    {
        if (property.Getter?.AccessModifier == BindingAccessModifier.Protected ||
            property.Setter?.AccessModifier == BindingAccessModifier.Protected)
        {
            return BindingAccessModifier.Protected;
        }

        return BindingAccessModifier.Public;
    }

    private static FunctionBinding? FindPrimaryBaseVirtualMatch(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { record.SymbolId };
        for (var current = record.GetPrimaryBaseRecord(compilation); current is not null; current = current.GetPrimaryBaseRecord(compilation))
        {
            if (!visited.Add(current.SymbolId))
            {
                break;
            }

            var match = current.Members
                .OfType<FunctionBinding>()
                .FirstOrDefault(candidate => !candidate.IsIgnored() && candidate.IsVirtual && RecordBindingExtensions.SignaturesMatch(candidate, function));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string RenderManagedCallbackParameter(ParameterBinding parameter)
    {
        ValidateVirtualCallbackSupport(parameter);
        return $"{GetManagedParameterAttribute(parameter.Type)}{GetManagedImportTypeName(parameter.Type)} {parameter.ManagedName}";
    }

    private static string BuildManagedCallbackInvocationArgument(BindingCompilation compilation, ParameterBinding parameter)
    {
        ValidateVirtualCallbackSupport(parameter);

        return parameter.Type.IsPointerType
            ? BuildManagedHandleWrap(compilation, parameter.Type, parameter.ManagedName, ownsHandle: false)
            : parameter.ManagedName;
    }

    private static string FormatReflectionParameterTypes(FunctionBinding method)
    {
        return method.Parameters.Count == 0
            ? "System.Array.Empty<global::System.Type>()"
            : $"new global::System.Type[] {{ {string.Join(", ", method.Parameters.Select(parameter => $"typeof({GetManagedWrapperTypeName(parameter.Type)})"))} }}";
    }

    private static string GetVirtualCallbackDelegateName(FunctionBinding method)
    {
        return $"__{GetVirtualCallbackTokenPrefix(method)}_Delegate";
    }

    private static string GetVirtualCallbackFieldName(FunctionBinding method)
    {
        return $"s_{GetVirtualCallbackTokenPrefix(method)}_Callback";
    }

    private static string GetVirtualCallbackPointerFieldName(FunctionBinding method)
    {
        return $"s_{GetVirtualCallbackTokenPrefix(method)}_Pointer";
    }

    private static string GetVirtualCallbackPointerAccessorName(FunctionBinding method)
    {
        return $"__Get{GetVirtualCallbackTokenPrefix(method)}Pointer";
    }

    private static string GetVirtualCallbackDispatchName(FunctionBinding method)
    {
        return $"__Dispatch{GetVirtualCallbackTokenPrefix(method)}";
    }

    private static string GetVirtualCallbackTokenPrefix(FunctionBinding method)
    {
        var ownerName = method.FindOwningRecord()?.ManagedName ?? "Global";
        return $"{SanitizeManagedToken(ownerName)}_{SanitizeManagedToken(method.ImportMethodName)}";
    }

    private static string SanitizeManagedToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString().Trim('_');
    }

    private static void ValidateVirtualCallbackSupport(FunctionBinding method)
    {
        foreach (var parameter in method.Parameters)
        {
            ValidateVirtualCallbackSupport(parameter);
        }

        if (method.ReturnType.Kind == BindingTypeKind.Handle && method.ReturnsOwnedHandle)
        {
            throw new NotSupportedException($"Director callback '{method.ManagedName}' does not support owned-handle returns yet.");
        }
    }

    private static void ValidateVirtualCallbackSupport(ParameterBinding parameter)
    {
        if (parameter.Direction is not BindingParameterDirection.In || parameter.AbiKind is not BindingParameterAbiKind.Value)
        {
            throw new NotSupportedException($"Director callback parameter '{parameter.ManagedName}' must be an in-value parameter.");
        }
    }

    private static string BuildManagedHandleWrap(BindingCompilation compilation, BindingType type, string handleExpression, bool ownsHandle)
    {
        var record = ResolveRecord(compilation, type);
        if (record is not null)
        {
            return $"{GetManagedWrapperTypeName(type)}.CreateManagedWrapper({handleExpression}, ownsHandle: {ownsHandle.ToString().ToLowerInvariant()})";
        }

        return ownsHandle
            ? $"new {type.ManagedTypeName}({handleExpression}, true)"
            : $"new {type.ManagedTypeName}({handleExpression})";
    }

    private static RecordBinding? ResolveRecord(BindingCompilation compilation, BindingType type)
    {
        return string.IsNullOrWhiteSpace(type.RecordId)
            ? null
            : compilation.Records.FirstOrDefault(candidate => string.Equals(candidate.SymbolId, type.RecordId, StringComparison.Ordinal));
    }

    private static string RenderManagedForwardingArgument(ParameterBinding parameter)
    {
        return parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => $"in {parameter.ManagedName}",
            BindingParameterDirection.Ref => $"ref {parameter.ManagedName}",
            BindingParameterDirection.Out => $"out {parameter.ManagedName}",
            _ => parameter.ManagedName
        };
    }

    private static string RenderManagedStaticTypeIdImportDeclaration(BindingCompilation compilation, RecordBinding record)
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "sharpcpp_e2e_{{SanitizeNativeEntrypointName(record.NativeName)}}_type_id", StringMarshalling = StringMarshalling.Utf8)]
            internal static partial nuint GetStaticTypeId();
            """;
    }

    private static string RenderManagedNativeObjectMetadataImportDeclaration(BindingCompilation compilation, RecordBinding record)
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "sharpcpp_e2e_{{SanitizeNativeEntrypointName(record.NativeName)}}_metadata", StringMarshalling = StringMarshalling.Utf8)]
            internal static partial void GetNativeObjectMetadata(nint self, out nint handle, out nuint dynamicTypeId);
            """;
    }

    private static string RenderNativeObjectRegistryInitializer(BindingCompilation compilation, IReadOnlyList<RecordBinding> records)
    {
        var writer = new CodeWriter();
        writer.Write(
            """
            internal static class __NativeObjectRegistryInitializer
            {
            private static readonly bool s_registered = RegisterAll();

            internal static void EnsureRegistered()
            {
            _ = s_registered;
            }

            private static bool RegisterAll()
            {
            """);

        foreach (var record in records.OrderBy(static record => record.ManagedName, StringComparer.Ordinal))
        {
            writer.Write($$"""
                global::SharpCpp.Support.NativeObjectRegistry.RegisterType<{{record.ManagedName}}>();
                """);
        }

        writer.Write(
            """
            return true;
            }
            }
            """);
        return writer.ToString();
    }


    private static string GetManagedImportMethodName(FunctionBinding function)
    {
        return function.ImportMethodName;
    }

    private static string SanitizeNativeEntrypointName(string value)
    {
        return value.Replace("::", "_", StringComparison.Ordinal)
                    .Replace("<", "_", StringComparison.Ordinal)
                    .Replace(">", "_", StringComparison.Ordinal);
    }

    private static string GetInstanceCacheFactoryExpression(RecordBinding record)
    {
        return string.IsNullOrWhiteSpace(record.ManagedInstanceCacheFactoryExpression)
            ? "new global::SharpCpp.Support.DefaultNativeInstanceCache()"
            : record.ManagedInstanceCacheFactoryExpression!;
    }

    private static string GetInstanceCacheFieldTypeName(RecordBinding record)
    {
        if (string.IsNullOrWhiteSpace(record.ManagedInstanceCacheFactoryExpression))
        {
            return "global::SharpCpp.Support.DefaultNativeInstanceCache";
        }

        return string.IsNullOrWhiteSpace(record.ManagedInstanceCacheTypeName)
            ? "global::SharpCpp.Support.INativeInstanceCache"
            : record.ManagedInstanceCacheTypeName!;
    }

    private static string GetManagedParameterAttribute(BindingType type)
    {
        return type.PrimitiveKind == BindingPrimitiveKind.Boolean
            ? "[MarshalAs(UnmanagedType.I1)] "
            : string.Empty;
    }

    private static string GetManagedReturnAttribute(BindingType type)
    {
        if (type.ManagedMarshaller is not null)
        {
            return $"[return: MarshalUsing(typeof({type.ManagedMarshaller}))]";
        }

        return type.PrimitiveKind == BindingPrimitiveKind.Boolean
            ? "[return: MarshalAs(UnmanagedType.I1)]"
            : string.Empty;
    }

    private static string GetManagedEnumUnderlyingTypeName(EnumBinding @enum)
    {
        return @enum.UnderlyingTypeName switch
        {
            "unsigned int" => "uint",
            "unsigned short" => "ushort",
            "unsigned long long" => "ulong",
            "int" => "int",
            "short" => "short",
            "long long" => "long",
            _ => "int"
        };
    }

    private static string NormalizeEnumManagedTypeName(string managedTypeName)
    {
        var normalized = managedTypeName
            .Replace("Const ", string.Empty, StringComparison.Ordinal)
            .Replace("const ", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (!normalized.Contains(' ', StringComparison.Ordinal) && !normalized.Contains('_', StringComparison.Ordinal))
        {
            return normalized;
        }

        return string.Concat(
            normalized
                .Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string RenderIgnoredStub(BindingNode node)
    {
        var reason = node.TryGetMetadata(MetadataKeys.IgnoreReason, out string? metadataReason)
            ? metadataReason!
            : "excluded";
        var declarationText = node.TryGetMetadata(MetadataKeys.DeclarationDisplayText, out string? metadataDeclaration)
            ? metadataDeclaration
            : string.Empty;

        return node switch
        {
            RecordBinding record => RenderIgnoredDeclarationStub(declarationText, $"{record.NativeName};", reason),
            EnumBinding @enum => RenderIgnoredDeclarationStub(declarationText, $"enum {@enum.NativeName};", reason),
            FunctionBinding function => RenderIgnoredFunctionStub(function, declarationText, reason),
            FieldBinding field => RenderIgnoredDeclarationStub(declarationText, $"{field.Type.CppTypeName} {field.NativeName};", reason),
            _ => $"// {node.Kind}; // excluded: {reason}"
        };
    }

    private static string RenderIgnoredDeclarationStub(string? declarationText, string fallback, string reason)
    {
        var declaration = string.IsNullOrWhiteSpace(declarationText) ? fallback : declarationText;
        return $"// {declaration} // excluded: {reason}";
    }

    private static string RenderIgnoredFunctionStub(FunctionBinding function, string? declarationText, string reason)
    {
        if (!string.IsNullOrWhiteSpace(declarationText))
        {
            return $"// {declarationText} // excluded: {reason}";
        }

        var parameterList = string.Join(", ", function.Parameters.Select(RenderIgnoredNativeParameter));
        var returnTypeName = function.CppReturnTypeName ?? function.ReturnType.CppTypeName;

        if (function.IsConstructor)
        {
            var constructorName = function.CppQualifiedName[(function.CppQualifiedName.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
            return $"// {constructorName}({parameterList}); // excluded: {reason}";
        }

        var staticModifier = function.IsStatic || function.Parent is null ? "static " : string.Empty;
        var qualifier = function.Parent is RecordBinding record ? $"{record.CppQualifiedName}::" : string.Empty;
        return $"// {staticModifier}{returnTypeName} {qualifier}{function.NativeName}({parameterList}); // excluded: {reason}";
    }

    private static bool CanEmitClone(RecordBinding record, bool isEffectivelyAbstract, bool canEmitDeleteThunk)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.HasCopyConstructor &&
            !isEffectivelyAbstract &&
            (!record.EmitDefaultNativeReleaseOverride || canEmitDeleteThunk);
    }

    private static bool InheritsCloneInterface(BindingCompilation compilation, RecordBinding record)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { record.SymbolId };
        for (var current = record.GetPrimaryBaseRecord(compilation); current is not null; current = current.GetPrimaryBaseRecord(compilation))
        {
            if (!visited.Add(current.SymbolId))
            {
                break;
            }

            if (current.HasCopyConstructor)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldBorrowFieldStorage(BindingCompilation compilation, FieldBinding field)
    {
        if (field.Type.Kind != BindingTypeKind.Handle || field.CppTypeName.EndsWith('*'))
        {
            return false;
        }

        return field.CppTypeName.Contains("vector<", StringComparison.Ordinal) ||
            field.CppTypeName.Contains("list<", StringComparison.Ordinal) ||
            field.CppTypeName.Contains("map<", StringComparison.Ordinal) ||
            field.CppTypeName.Contains("unordered_map<", StringComparison.Ordinal);
    }

    private static string RenderNativeBorrowedFieldGetInvocation(FieldBinding field)
    {
        return $$"""
                Marshaller<{{field.OwningRecordCppTypeName}}*> self_marshaller;
                auto* instance = static_cast<{{field.OwningRecordCppTypeName}}*>(self_marshaller.ConvertToCpp(self));
                return &(instance->{{field.NativeName}});
                """;
    }

    private static string RenderNativeBorrowedFieldSetInvocation(FieldBinding field)
    {
        return $$"""
                Marshaller<{{field.OwningRecordCppTypeName}}*> self_marshaller;
                auto* instance = static_cast<{{field.OwningRecordCppTypeName}}*>(self_marshaller.ConvertToCpp(self));
                if (value == nullptr)
                {
                instance->{{field.NativeName}}.clear();
                return;
                }

                auto* source = static_cast<{{field.CppTypeName}}*>(value);
                instance->{{field.NativeName}} = std::move(*source);
                """;
    }

    private static string RenderNativeMarshallerExposedInvocation(FunctionBinding method, string marshallerCppType, string abiArguments)
    {
        var signatureParameters = string.Join(", ", new[] { $"{method.OwningRecordCppTypeName}*" }.Concat(method.Parameters.Select(static parameter => parameter.CppTypeName)));
        var lambdaParameters = string.Join(", ", new[] { $"{method.OwningRecordCppTypeName}* __self" }
            .Concat(method.Parameters.Select((parameter, index) => $"{parameter.CppTypeName} __arg{index}")));
        var invokeArguments = string.Join(", ", new[] { "*__self" }
            .Concat(method.Parameters.Select((_, index) => $"__arg{index}")));
        var callArguments = string.IsNullOrWhiteSpace(abiArguments) ? "self" : $"self, {abiArguments}";
        var returnStatement = method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? $"Marshaller<{marshallerCppType}>::{method.NativeName}({invokeArguments});"
            : $"return Marshaller<{marshallerCppType}>::{method.NativeName}({invokeArguments});";
        var dispatch = method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? $"MarshallerInvoker::invoke_as<FT>(__invoke, {callArguments});"
            : $"return MarshallerInvoker::invoke_as<FT>(__invoke, {callArguments});";

        return $$"""
            using FT = {{method.CppReturnTypeName}}({{signatureParameters}});
            auto __invoke = []({{lambdaParameters}}) -> {{method.CppReturnTypeName}}
            {
            {{returnStatement}}
            };
            {{dispatch}}
            """;
    }

    private static string RenderIgnoredNativeParameter(ParameterBinding parameter)
        => $"{parameter.CppTypeName} {parameter.NativeName}";

    private static void WriteDocumentationComment(CodeWriter writer, BindingNode node)
    {
        if (!node.TryGetMetadata(MetadataKeys.DocumentationComment, out string? comment) || string.IsNullOrWhiteSpace(comment))
        {
            return;
        }

        var escaped = System.Security.SecurityElement.Escape(comment!);
        writer.Write($"/// <summary>{escaped}</summary>");
    }

    private static void AssignImportMethodNames(BindingCompilation compilation)
    {
        foreach (var group in compilation.Members.OfType<FunctionBinding>()
            .Where(static function => function.Parent is not RecordBinding)
            .GroupBy(static function => function.ManagedName, StringComparer.Ordinal))
        {
            var counter = 0;
            foreach (var function in group.OrderBy(static f => f.SymbolId, StringComparer.Ordinal))
            {
                counter++;
                function.ImportMethodName = counter == 1 ? function.ManagedName : $"{function.ManagedName}{counter}";
            }
        }

        foreach (var record in compilation.Records)
        {
            foreach (var group in record.Members.OfType<FunctionBinding>()
                .GroupBy(static function => function.ManagedName, StringComparer.Ordinal))
            {
                var counter = 0;
                foreach (var function in group.OrderBy(static f => f.SymbolId, StringComparer.Ordinal))
                {
                    counter++;
                    function.ImportMethodName = counter == 1 ? function.ManagedName : $"{function.ManagedName}{counter}";
                }
            }
        }
    }

    private static bool HasUnexpressibleDefaultParams(FunctionBinding function)
    {
        return function.Parameters.Any(static parameter => parameter.HasMetadata(MetadataKeys.UnexpressibleDefault));
    }

    private static bool HasUndefaultableParams(FunctionBinding function)
    {
        return function.Parameters.Any(static parameter => parameter.HasMetadata(MetadataKeys.UndefaultableParam));
    }

    private static int GetFirstUndefaultableParamIndex(FunctionBinding function)
    {
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].HasMetadata(MetadataKeys.UndefaultableParam))
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyList<int> GetUnexpressibleDefaultParameterIndices(FunctionBinding function)
    {
        var indices = new List<int>();
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].HasMetadata(MetadataKeys.UnexpressibleDefault))
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    private static IReadOnlyList<int> GetVariantParameterIndices(FunctionBinding function)
    {
        var indices = new HashSet<int>(GetUnexpressibleDefaultParameterIndices(function));
        var firstUndefaultable = GetFirstUndefaultableParamIndex(function);
        if (firstUndefaultable >= 0)
        {
            indices.Add(firstUndefaultable);
        }

        return indices.OrderBy(static i => i).ToArray();
    }

    private static IReadOnlyList<ParameterBinding> GetUnexpressibleVariantParams(FunctionBinding function, int keepCount)
    {
        return function.Parameters.Take(keepCount).ToArray();
    }

    private static string GetUnexpressibleVariantImportName(string importMethodName, int keepCount)
    {
        return $"{importMethodName}__Args{keepCount}";
    }

    private static string GetUnexpressibleVariantEntrypointName(string entryPointName, int keepCount)
    {
        return $"{entryPointName}__Args{keepCount}";
    }

    private static string RenderUnexpressibleDefaultFreeFunctionImport(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var importMethodName = GetManagedImportMethodName(function);
        foreach (var keepCount in GetVariantParameterIndices(function))
        {
            var variantParams = GetUnexpressibleVariantParams(function, keepCount);
            var parameterList = string.Join(", ", variantParams.Select(RenderManagedParameter));
            var returnTypeName = GetManagedImportTypeName(function.ReturnType);
            var returnAttribute = GetManagedReturnAttribute(function.ReturnType);
            var importName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            var entrypointName = GetUnexpressibleVariantEntrypointName(function.EntryPointName, keepCount);
            var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
                ? $"internal static partial {returnTypeName} {importName}({parameterList});"
                : $"{returnAttribute}\ninternal static partial {returnTypeName} {importName}({parameterList});";

            writer.Write($$"""
                [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{entrypointName}}", StringMarshalling = StringMarshalling.Utf8)]
                {{importMethodLine}}
                """);
        }
        return writer.ToString();
    }

    private static string RenderManagedUnexpressibleDefaultStaticImports(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var importMethodName = GetManagedImportMethodName(function);
        foreach (var keepCount in GetVariantParameterIndices(function))
        {
            var variantParams = GetUnexpressibleVariantParams(function, keepCount);
            var parameterList = string.Join(", ", variantParams.Select(RenderManagedParameter));
            var returnTypeName = GetManagedImportTypeName(function.ReturnType);
            var returnAttribute = GetManagedReturnAttribute(function.ReturnType);
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            var variantEntrypointName = GetUnexpressibleVariantEntrypointName(function.EntryPointName, keepCount);
            var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
                ? $"internal static partial {returnTypeName} {variantImportName}({parameterList});"
                : $"{returnAttribute}\ninternal static partial {returnTypeName} {variantImportName}({parameterList});";

            writer.Write($$"""
                [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{variantEntrypointName}}", StringMarshalling = StringMarshalling.Utf8)]
                {{importMethodLine}}
                """);
        }

        return writer.ToString();
    }

    private static string RenderManagedUnexpressibleDefaultInstanceImports(BindingCompilation compilation, FunctionBinding function, string recordManagedName, bool useCallbase = false)
    {
        var writer = new CodeWriter();
        var importMethodName = useCallbase
            ? GetManagedCallbaseImportMethodName(function)
            : GetManagedImportMethodName(function);
        var owningRecord = function.FindOwningRecord();
        var entryPointName = useCallbase && owningRecord is not null
            ? GetNativeCallbaseEntrypointName(owningRecord, function)
            : function.EntryPointName;

        foreach (var keepCount in GetVariantParameterIndices(function))
        {
            var variantParams = GetUnexpressibleVariantParams(function, keepCount);
            var parameterList = string.Join(", ", new[] { $"{recordManagedName} self" }.Concat(variantParams.Select(RenderManagedParameter)));
            var returnTypeName = GetManagedImportTypeName(function.ReturnType);
            var returnAttribute = GetManagedReturnAttribute(function.ReturnType);
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            var variantEntrypointName = GetUnexpressibleVariantEntrypointName(entryPointName, keepCount);
            var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
                ? $"internal static partial {returnTypeName} {variantImportName}({parameterList});"
                : $"{returnAttribute}\ninternal static partial {returnTypeName} {variantImportName}({parameterList});";

            writer.Write($$"""
                [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{variantEntrypointName}}", StringMarshalling = StringMarshalling.Utf8)]
                {{importMethodLine}}
                """);
        }

        return writer.ToString();
    }

    private static string RenderManagedUnexpressibleDefaultConstructorImports(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var importMethodName = GetManagedImportMethodName(function);
        foreach (var keepCount in GetVariantParameterIndices(function))
        {
            var variantParams = GetUnexpressibleVariantParams(function, keepCount);
            var parameterList = string.Join(", ", variantParams.Select(RenderManagedParameter));
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            var variantEntrypointName = GetUnexpressibleVariantEntrypointName(function.EntryPointName, keepCount);

            writer.Write($$"""
                [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{variantEntrypointName}}", StringMarshalling = StringMarshalling.Utf8)]
                internal static partial nint {{variantImportName}}({{parameterList}});
                """);
        }

        return writer.ToString();
    }

    private static void EmitUnexpressibleDefaultVariant(
        CodeWriter writer,
        FunctionBinding function,
        IReadOnlyList<ParameterBinding> variantParams,
        int keepCount,
        string entryPointBaseName,
        string invocation,
        string? selfCppTypeName = null)
    {
        var isSelf = selfCppTypeName is not null;

        var parameterParts = isSelf
            ? new[] { $"arg_t<{selfCppTypeName}*> self" }.Concat(variantParams.Select(RenderNativeParameter))
            : variantParams.Select(RenderNativeParameter);
        var parameterList = string.Join(", ", parameterParts);

        var abiArgs = string.Join(", ", variantParams.Select(static p => p.NativeName));

        var entrypointName = GetUnexpressibleVariantEntrypointName(entryPointBaseName, keepCount);

        var cppTypeParts = isSelf
            ? new[] { $"{selfCppTypeName}*" }.Concat(variantParams.Select(static p => p.CppTypeName))
            : variantParams.Select(static p => p.CppTypeName);
        var cppTypeNames = string.Join(", ", cppTypeParts);

        var paramDeclParts = isSelf
            ? new[] { "auto&& self" }.Concat(variantParams.Select(static p => $"auto&& {p.NativeName}"))
            : variantParams.Select(static p => $"auto&& {p.NativeName}");
        var cppParamDecls = string.Join(", ", paramDeclParts);

        var signature = $"{function.CppReturnTypeName}({cppTypeNames})";

        var invokeAsArgs = isSelf
            ? $"fn, self{(abiArgs.Length > 0 ? $", {abiArgs}" : string.Empty)}"
            : $"fn{(abiArgs.Length > 0 ? $", {abiArgs}" : string.Empty)}";

        writer.Write($$"""
            SHARPCPP_EXPORT ret_t<{{function.CppReturnTypeName}}> {{entrypointName}}({{parameterList}})
            {
                using FT = {{signature}};
                auto fn = []({{cppParamDecls}}) -> decltype(auto)
                {
                    {{invocation}}
                };
                return MarshallerInvoker::invoke_as<FT>({{invokeAsArgs}});
            }

            """);
    }

    private static string ForwardVariantArgs(IReadOnlyList<ParameterBinding> variantParams)
    {
        return string.Join(", ", variantParams.Select(static p => $"std::forward<decltype({p.NativeName})>({p.NativeName})"));
    }

    private static string RenderNativeUnexpressibleDefaultFreeFunction(FunctionBinding function)
    {
        var writer = new CodeWriter();
        foreach (var keepCount in GetVariantParameterIndices(function))
        {
            var variantParams = GetUnexpressibleVariantParams(function, keepCount);
            var invocation = $"return {function.CppQualifiedName}({ForwardVariantArgs(variantParams)});";
            EmitUnexpressibleDefaultVariant(writer, function, variantParams, keepCount, function.EntryPointName, invocation);
        }
        return writer.ToString();
    }

    private static string RenderNativeUnexpressibleDefaultStaticMethod(FunctionBinding method)
    {
        var writer = new CodeWriter();
        foreach (var keepCount in GetVariantParameterIndices(method))
        {
            var variantParams = GetUnexpressibleVariantParams(method, keepCount);
            var invocation = $"return {method.OwningRecordCppTypeName}::{method.NativeName}({ForwardVariantArgs(variantParams)});";
            EmitUnexpressibleDefaultVariant(writer, method, variantParams, keepCount, method.EntryPointName, invocation);
        }
        return writer.ToString();
    }

    private static string RenderNativeUnexpressibleDefaultInstanceMethod(FunctionBinding method)
    {
        if (method.AccessModifier == BindingAccessModifier.Protected)
        {
            throw new NotSupportedException($"Protected member '{method.ManagedName}' does not support unexpressible defaults yet.");
        }

        var writer = new CodeWriter();
        foreach (var keepCount in GetVariantParameterIndices(method))
        {
            var variantParams = GetUnexpressibleVariantParams(method, keepCount);
            var forwardedArgs = ForwardVariantArgs(variantParams);
            var invocation = method.NativeDispatchRecordCppTypeName is null
                ? $"return std::forward<decltype(self)>(self)->{method.NativeName}({forwardedArgs});"
                : $$"""
                    auto* dispatch = dynamic_cast<{{method.NativeDispatchRecordCppTypeName}}*>(std::forward<decltype(self)>(self));
                    if (dispatch == nullptr)
                    {
                        throw std::runtime_error("SharpCpp failed to adjust the native handle for secondary-base dispatch.");
                    }
                    return dispatch->{{method.NativeName}}({{forwardedArgs}});
                    """;
            EmitUnexpressibleDefaultVariant(writer, method, variantParams, keepCount, method.EntryPointName, invocation, method.OwningRecordCppTypeName);
        }
        return writer.ToString();
    }

    private static string RenderNativeUnexpressibleDefaultCallbase(RecordBinding record, FunctionBinding method)
    {
        var writer = new CodeWriter();
        var helperName = GetNativeCallbaseHelperName(method);
        var baseEntrypointName = GetNativeCallbaseEntrypointName(record, method);

        foreach (var keepCount in GetVariantParameterIndices(method))
        {
            var variantParams = GetUnexpressibleVariantParams(method, keepCount);
            var forwardedArgs = ForwardVariantArgs(variantParams);
            var invocation = $"return {helperName}(std::forward<decltype(self)>(self){(forwardedArgs.Length > 0 ? $", {forwardedArgs}" : string.Empty)});";
            EmitUnexpressibleDefaultVariant(writer, method, variantParams, keepCount, baseEntrypointName, invocation, record.CppQualifiedName);
        }
        return writer.ToString();
    }

    private static string RenderNativeUnexpressibleDefaultConstructor(FunctionBinding constructor, bool requiresDirector)
    {
        var writer = new CodeWriter();
        var constructedTypeName = requiresDirector
            ? GetNativeDirectorClassName((RecordBinding)constructor.Parent!)
            : constructor.OwningRecordCppTypeName;

        foreach (var keepCount in GetVariantParameterIndices(constructor))
        {
            var variantParams = GetUnexpressibleVariantParams(constructor, keepCount);
            var invocation = $"return new {constructedTypeName}({ForwardVariantArgs(variantParams)});";
            EmitUnexpressibleDefaultVariant(writer, constructor, variantParams, keepCount, constructor.EntryPointName, invocation);
        }
        return writer.ToString();
    }

    private static string BuildManagedDispatchArgument(ParameterBinding parameter, string plannedArgument)
    {
        if (!parameter.HasMetadata(MetadataKeys.UnexpressibleDefault) || parameter.Type.IsPointerType)
        {
            return plannedArgument;
        }

        var valueExpression = $"{parameter.ManagedName}.Value";
        if (!parameter.Type.IsPointerType)
        {
            return parameter.Direction switch
            {
                BindingParameterDirection.Ref => $"ref {valueExpression}",
                BindingParameterDirection.Out => $"out {valueExpression}",
                _ => valueExpression
            };
        }
        return parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => $"in {valueExpression}",
            BindingParameterDirection.Ref => $"ref {valueExpression}",
            BindingParameterDirection.Out => $"out {valueExpression}",
            _ => valueExpression
        };
    }

    private static string BuildManagedDispatchArgumentRequired(ParameterBinding parameter, string plannedArgument)
    {
        // For required (non-nullable) params, just use the planned argument directly
        return plannedArgument;
    }

    private static string BuildManagedDispatchArguments(FunctionBinding function, ManagedInvocationPlan plan, IReadOnlyList<string> leadingArguments, int keepCount)
    {
        var arguments = new List<string>(leadingArguments);
        for (var i = 0; i < keepCount; i++)
        {
            var parameter = function.Parameters[i];
            arguments.Add(BuildManagedDispatchArgument(parameter, plan.Arguments[i]));
        }

        return string.Join(", ", arguments);
    }

    private static string BuildUnexpressibleDefaultCondition(ParameterBinding parameter, bool provided)
    {
        if (parameter.Type.IsPointerType)
        {
            return $"{parameter.ManagedName} is {(provided ? "not null" : "null")}";
        }

        return provided
            ? $"{parameter.ManagedName}.HasValue"
            : $"!{parameter.ManagedName}.HasValue";
    }

    private static string BuildUnexpressibleDefaultCondition(FunctionBinding function, IReadOnlyList<int> defaultIndices, int providedDefaultCount)
    {
        var parts = new List<string>(defaultIndices.Count);
        for (var i = 0; i < defaultIndices.Count; i++)
        {
            parts.Add(BuildUnexpressibleDefaultCondition(function.Parameters[defaultIndices[i]], provided: i < providedDefaultCount));
        }

        return string.Join(" && ", parts);
    }

    private static void WriteUndefaultableOverloads(
        BindingCompilation compilation,
        CodeWriter writer,
        FunctionBinding function,
        string importMethodName,
        IReadOnlyList<string> leadingArguments,
        bool isConstructor = false)
    {
        var firstU = GetFirstUndefaultableParamIndex(function);
        if (firstU < 0)
        {
            // No undefaultable params — normal path, shouldn't reach here
            return;
        }

        // Build common invocation plan for the full parameter set
        var fullPlan = BuildManagedInvocationPlan(compilation, function.Parameters);

        // =========================================================
        // Overload A: params before first undefaultable index
        // =========================================================
        {
            var beforeParams = function.Parameters.Take(firstU).ToArray();

            // Check if before-params have any unexpressible defaults
            var beforeHasUnexpressible = beforeParams.Any(p => p.HasMetadata(MetadataKeys.UnexpressibleDefault));

            // Render public signature for overload A
            var aParamList = string.Join(", ", beforeParams.Select(RenderManagedPublicParameter));

            if (isConstructor)
            {
                writer.Write($$"""
                    public {{function.ManagedName}}({{aParamList}})
                    {
                    """);
            }
            else if (function.IsStatic)
            {
                writer.Write($$"""
                    {{GetManagedVisibility(function.AccessModifier)}} static {{GetManagedPublicReturnTypeName(function.ReturnType)}} {{function.ManagedName}}({{aParamList}})
                    {
                    """);
            }
            else
            {
                writer.Write($$"""
                    {{GetManagedVisibility(function.AccessModifier)}} {{GetManagedPublicReturnTypeName(function.ReturnType)}} {{function.ManagedName}}({{aParamList}})
                    {
                    """);
            }

            if (firstU == 0)
            {
                // Overload A has no params — call __Args0 variant
                var variantImportName = GetUnexpressibleVariantImportName(importMethodName, 0);
                if (isConstructor)
                {
                    writer.Write($$"""
                        var __handle = __PInvoke.{{variantImportName}}();
                        """);
                }
                else if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
                {
                    writer.Write($$"""
                        __PInvoke.{{variantImportName}}({{string.Join(", ", leadingArguments)}});
                        """);
                }
                else
                {
                    var leading = leadingArguments.Count > 0 ? string.Join(", ", leadingArguments) + ", " : string.Empty;
                    writer.Write($$"""
                        var __result = __PInvoke.{{variantImportName}}({{leading.TrimEnd(',', ' ')}});
                        """);
                }
            }
            else if (beforeHasUnexpressible)
            {
                // Cascade for before-params with unexpressible defaults
                WriteUnexpressibleDefaultCascade(compilation, writer, function, importMethodName, leadingArguments);
                // Need to get past the closing brace
            }
            else
            {
                // Direct call with firstU params — use __Args{firstU} variant
                var variantImportName = GetUnexpressibleVariantImportName(importMethodName, firstU);
                var beforePlan = BuildManagedInvocationPlan(compilation, beforeParams);
                var beforeArgs = string.Join(", ", leadingArguments.Concat(beforePlan.Arguments));

                if (isConstructor)
                {
                    writer.Write($$"""
                        var __handle = __PInvoke.{{variantImportName}}({{beforeArgs}});
                        """);
                }
                else if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
                {
                    writer.Write($$"""
                        __PInvoke.{{variantImportName}}({{beforeArgs}});
                        """);
                }
                else
                {
                    writer.Write($$"""
                        var __result = __PInvoke.{{variantImportName}}({{beforeArgs}});
                        """);
                }
            }

            // Handle return and constructor post-call
            if (isConstructor)
            {
                writer.Write("""
                    return __handle;
                    }
                    """);
            }
            else if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.Write("""
                    }
                    """);
            }
            else
            {
                writer.Write($$"""
                    return {{BuildManagedHandleWrapIfNeeded(compilation, function.ReturnType, "__result", function.ReturnsOwnedHandle)}};
                    }
                    """);
            }

            writer.WriteLine();
        }

        // =========================================================
        // Overload B: all params, undefaultable ones required
        // =========================================================
        {
            // Render public parameter list: params before firstU are required,
            // firstU is required, params after firstU get normal defaults
            var bParamParts = new List<string>();
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var param = function.Parameters[i];
                if (i < firstU || param.HasMetadata(MetadataKeys.UndefaultableParam))
                {
                    // Required: no default, non-nullable
                    bParamParts.Add(RenderManagedPublicParameterRequired(param));
                }
                else
                {
                    // Normal default rules
                    bParamParts.Add(RenderManagedPublicParameter(param));
                }
            }
            var bParamList = string.Join(", ", bParamParts);

            if (isConstructor)
            {
                writer.Write($$"""
                    public {{function.ManagedName}}({{bParamList}})
                    {
                    """);
            }
            else if (function.IsStatic)
            {
                writer.Write($$"""
                    {{GetManagedVisibility(function.AccessModifier)}} static {{GetManagedPublicReturnTypeName(function.ReturnType)}} {{function.ManagedName}}({{bParamList}})
                    {
                    """);
            }
            else
            {
                writer.Write($$"""
                    {{GetManagedVisibility(function.AccessModifier)}} {{GetManagedPublicReturnTypeName(function.ReturnType)}} {{function.ManagedName}}({{bParamList}})
                    {
                    """);
            }

            // Emit pre-call statements
            foreach (var statement in fullPlan.PreCallStatements)
            {
                writer.Write(statement);
            }

            // Check if after-firstU params have unexpressible defaults
            var afterParams = function.Parameters.Skip(firstU).ToArray();
            var afterHasUnexpressible = afterParams.Any(p => p.HasMetadata(MetadataKeys.UnexpressibleDefault));

            if (afterHasUnexpressible)
            {
                // Build a cascade for the after-params portion (indices >= firstU)
                // This mirrors the standard cascade but only for after-firstU params.
                // Before-firstU params are always provided (required, non-nullable).
                var afterDefaultIndices = new List<int>();
                for (var i = firstU; i < function.Parameters.Count; i++)
                {
                    if (function.Parameters[i].HasMetadata(MetadataKeys.UnexpressibleDefault))
                    {
                        afterDefaultIndices.Add(i);
                    }
                }

                // Each branch handles its own dispatch + post-call + return
                for (var providedDefaultCount = afterDefaultIndices.Count; providedDefaultCount >= 0; providedDefaultCount--)
                {
                    var keepCount = providedDefaultCount < afterDefaultIndices.Count
                        ? afterDefaultIndices[providedDefaultCount]
                        : function.Parameters.Count;
                    WriteUndefaultableBranchB(compilation, writer, function, importMethodName, fullPlan,
                        leadingArguments, keepCount, afterDefaultIndices, providedDefaultCount);
                }

                // Fallback: should never reach here if cascade conditions are complete
                writer.Write(
                    """
                        throw new global::System.ArgumentException("SharpCpp optional arguments that depend on native defaults must be supplied as a trailing contiguous suffix.");
                    """);
            }
            else
            {
                // Direct call with all params
                var allArgs = string.Join(", ", leadingArguments.Concat(fullPlan.Arguments));

                if (isConstructor)
                {
                    writer.Write($$"""
                        var __handle = __PInvoke.{{importMethodName}}({{allArgs}});
                        """);
                }
                else if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
                {
                    writer.Write($$"""
                        __PInvoke.{{importMethodName}}({{allArgs}});
                        """);
                }
                else
                {
                    writer.Write($$"""
                        var __result = __PInvoke.{{importMethodName}}({{allArgs}});
                        """);
                }

                // Post-call statements (only on non-cascade path)
                foreach (var statement in fullPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

                // Return / constructor finalize (only on non-cascade path)
                if (isConstructor)
                {
                    writer.Write("""
                        return __handle;
                        }
                        """);
                }
                else if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
                {
                    writer.Write("""
                        }
                        """);
                }
                else
                {
                    writer.Write($$"""
                        return {{BuildManagedHandleWrapIfNeeded(compilation, function.ReturnType, "__result", function.ReturnsOwnedHandle)}};
                        }
                        """);
                }
            }
        }
    }

    private static void WriteUndefaultableBranchB(
        BindingCompilation compilation,
        CodeWriter writer,
        FunctionBinding function,
        string importMethodName,
        ManagedInvocationPlan plan,
        IReadOnlyList<string> leadingArguments,
        int keepCount,
        IReadOnlyList<int> defaultIndices,
        int? providedDefaultCount = null)
    {
        // For Overload B cascade branches, the before-firstU params are always present (non-nullable).
        // The cascade condition only tests the after-firstU params that have unexpressible defaults.
        if (providedDefaultCount.HasValue)
        {
            // Build condition for the unexpressible after-params
            var relevantIndices = defaultIndices.Where(i => i >= GetFirstUndefaultableParamIndex(function)).ToList();
            if (relevantIndices.Count == 0)
            {
                // No cascade needed
                return;
            }

            var condition = BuildUndefaultableBCondition(function, relevantIndices, providedDefaultCount.Value);
            writer.WriteLine($"if ({condition})");
            writer.WriteLine("{");
        }

        // Build dispatch arguments — for required params, use the plan argument directly;
        // for unexpressible default after-params, unwrap .Value
        var args = new List<string>(leadingArguments);
        for (var i = 0; i < keepCount; i++)
        {
            var param = function.Parameters[i];
            if (param.HasMetadata(MetadataKeys.UndefaultableParam) ||
                (i < GetFirstUndefaultableParamIndex(function) && !param.HasMetadata(MetadataKeys.UnexpressibleDefault)))
            {
                // Required param — use plan argument directly
                args.Add(plan.Arguments[i]);
            }
            else
            {
                // May have unexpressible default — use .Value unwrapping if needed
                args.Add(BuildManagedDispatchArgument(param, plan.Arguments[i]));
            }
        }
        var invokeArgs = string.Join(", ", args);

        if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.Write($$"""
                __PInvoke.{{importMethodName}}({{invokeArgs}});
                """);

            foreach (var statement in plan.PostCallStatements)
            {
                writer.Write(statement);
            }

            // Prevent fallthrough to next cascade branch or throw
            if (providedDefaultCount.HasValue)
            {
                writer.WriteLine("return;");
            }
        }
        else
        {
            writer.Write($$"""
                var __result = __PInvoke.{{importMethodName}}({{invokeArgs}});
                """);

            foreach (var statement in plan.PostCallStatements)
            {
                writer.Write(statement);
            }

            writer.Write($$"""
                return {{BuildManagedHandleWrapIfNeeded(compilation, function.ReturnType, "__result", function.ReturnsOwnedHandle)}};
                """);
        }

        if (providedDefaultCount.HasValue)
        {
            writer.WriteLine("}");
        }
    }

    private static string BuildUndefaultableBCondition(FunctionBinding function, IReadOnlyList<int> defaultIndices, int providedDefaultCount)
    {
        var firstU = GetFirstUndefaultableParamIndex(function);
        var relevantIndices = defaultIndices.Where(i => i >= firstU).ToList();
        var parts = new List<string>(relevantIndices.Count);
        for (var i = 0; i < relevantIndices.Count; i++)
        {
            var param = function.Parameters[relevantIndices[i]];
            parts.Add(param.HasMetadata(MetadataKeys.UndefaultableParam)
                ? $"{param.ManagedName} is not null"
                : BuildUnexpressibleDefaultCondition(param, provided: i < providedDefaultCount));
        }

        return string.Join(" && ", parts);
    }

    private static string BuildManagedHandleWrapIfNeeded(BindingCompilation compilation, BindingType type, string variableName, bool returnsOwnedHandle)
    {
        if (type.Kind != BindingTypeKind.Handle)
        {
            return variableName;
        }

        return BuildManagedHandleWrap(compilation, type, variableName, returnsOwnedHandle);
    }

    private static void WriteUnexpressibleDefaultCascade(BindingCompilation compilation, CodeWriter writer, FunctionBinding function, string importMethodName, IReadOnlyList<string> leadingArguments)
    {
        var defaultIndices = GetUnexpressibleDefaultParameterIndices(function);
        var invocationPlan = BuildManagedInvocationPlan(compilation, function.Parameters);

        foreach (var statement in invocationPlan.PreCallStatements)
        {
            writer.Write(statement);
        }

        WriteUnexpressibleDefaultBranch(compilation, writer, function, importMethodName, invocationPlan, leadingArguments, function.Parameters.Count, defaultIndices, defaultIndices.Count);

        for (var providedDefaultCount = defaultIndices.Count - 1; providedDefaultCount >= 0; providedDefaultCount--)
        {
            var keepCount = defaultIndices[providedDefaultCount];
            var variantPlan = BuildManagedInvocationPlan(compilation, GetUnexpressibleVariantParams(function, keepCount));
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            WriteUnexpressibleDefaultBranch(compilation, writer, function, variantImportName, variantPlan, leadingArguments, keepCount, defaultIndices, providedDefaultCount);
        }

        writer.Write(
            """
                throw new global::System.ArgumentException("SharpCpp optional arguments that depend on native defaults must be supplied as a trailing contiguous suffix.");
            """);
    }

    private static void WriteUnexpressibleDefaultBranch(BindingCompilation compilation, CodeWriter writer, FunctionBinding function, string importName,
        ManagedInvocationPlan plan, IReadOnlyList<string> leadingArguments, int keepCount, IReadOnlyList<int> defaultIndices, int providedDefaultCount)
    {
        writer.WriteLine($"if ({BuildUnexpressibleDefaultCondition(function, defaultIndices, providedDefaultCount)})");
        writer.WriteLine("{");
        WriteDispatchCall(writer, compilation, function, importName, plan, leadingArguments, keepCount);
        if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.WriteLine("return;");
        }

        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void WriteDispatchCall(CodeWriter writer, BindingCompilation compilation, FunctionBinding function, string importName,
        ManagedInvocationPlan plan, IReadOnlyList<string> leadingArguments, int keepCount)
    {
        var resultVar = "__result";
        var args = BuildManagedDispatchArguments(function, plan, leadingArguments, keepCount);

        if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.Write($$"""
                __PInvoke.{{importName}}({{args}});
                """);

            foreach (var statement in plan.PostCallStatements)
                writer.Write(statement);
        }
        else if (function.ReturnType.Kind == BindingTypeKind.Handle)
        {
            writer.Write($$"""
                var {{resultVar}} = __PInvoke.{{importName}}({{args}});
                """);

            foreach (var statement in plan.PostCallStatements)
                writer.Write(statement);

            writer.Write($$"""
                return {{BuildManagedHandleWrap(compilation, function.ReturnType, resultVar, function.ReturnsOwnedHandle)}};
                """);
        }
        else
        {
            writer.Write($$"""
                var {{resultVar}} = __PInvoke.{{importName}}({{args}});
                """);

            foreach (var statement in plan.PostCallStatements)
                writer.Write(statement);

            writer.Write($$"""
                return {{resultVar}};
                """);
        }
    }

    private static void WriteUnexpressibleDefaultConstructorCascade(CodeWriter writer, FunctionBinding function, string importMethodName)
    {
        var defaultIndices = GetUnexpressibleDefaultParameterIndices(function);
        var invocationPlan = BuildManagedInvocationPlan(null!, function.Parameters);

        foreach (var statement in invocationPlan.PreCallStatements)
        {
            writer.Write(statement);
        }

        WriteUnexpressibleDefaultConstructorBranch(writer, function, importMethodName, invocationPlan, function.Parameters.Count, defaultIndices, defaultIndices.Count);

        for (var providedDefaultCount = defaultIndices.Count - 1; providedDefaultCount >= 0; providedDefaultCount--)
        {
            var keepCount = defaultIndices[providedDefaultCount];
            var variantPlan = BuildManagedInvocationPlan(null!, GetUnexpressibleVariantParams(function, keepCount));
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            WriteUnexpressibleDefaultConstructorBranch(writer, function, variantImportName, variantPlan, keepCount, defaultIndices, providedDefaultCount);
        }

        writer.Write(
            """
                throw new global::System.ArgumentException("SharpCpp optional arguments that depend on native defaults must be supplied as a trailing contiguous suffix.");
            """);
    }

    private static void WriteUndefaultableConstructorCascade_Filtered(
        CodeWriter writer,
        FunctionBinding function,
        string importMethodName,
        int firstU,
        IReadOnlyList<int> afterDefaultIndices)
    {
        if (afterDefaultIndices.Count == 0)
        {
            return;
        }

        // Use only the full-params import variant for Overload B (before-firstU params
        // are always present, so we never call __ArgsN for before-firstU positions).
        var invocationPlan = BuildManagedInvocationPlan(null!, function.Parameters);

        foreach (var statement in invocationPlan.PreCallStatements)
        {
            writer.Write(statement);
        }

        // Branch: all after-defaults provided → full dispatch
        var conditionAll = BuildUndefaultableBCondition(function, afterDefaultIndices, afterDefaultIndices.Count);
        writer.WriteLine($"if ({conditionAll})");
        writer.WriteLine("{");
        WriteConstructorDispatchCall(writer, function, importMethodName, invocationPlan, function.Parameters.Count);
        writer.WriteLine("}");
        writer.WriteLine();

        // Cascade branches: progressively fewer after-defaults provided
        // Each uses a __ArgsN variant that keeps only up to the relevant after-default index
        for (var providedDefaultCount = afterDefaultIndices.Count - 1; providedDefaultCount >= 0; providedDefaultCount--)
        {
            var keepCount = afterDefaultIndices[providedDefaultCount];
            var condition = BuildUndefaultableBCondition(function, afterDefaultIndices, providedDefaultCount);
            writer.WriteLine($"if ({condition})");
            writer.WriteLine("{");
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            var variantPlan = BuildManagedInvocationPlan(null!, GetUnexpressibleVariantParams(function, keepCount));
            WriteConstructorDispatchCall(writer, function, variantImportName, variantPlan, keepCount);
            writer.WriteLine("}");
            writer.WriteLine();
        }

        writer.Write(
            """
                throw new global::System.ArgumentException("SharpCpp optional arguments that depend on native defaults must be supplied as a trailing contiguous suffix.");
            """);
    }

    private static void WriteUnexpressibleDefaultConstructorBranch(CodeWriter writer, FunctionBinding function, string importName,
        ManagedInvocationPlan plan, int keepCount, IReadOnlyList<int> defaultIndices, int providedDefaultCount)
    {
        writer.WriteLine($"if ({BuildUnexpressibleDefaultCondition(function, defaultIndices, providedDefaultCount)})");
        writer.WriteLine("{");
        WriteConstructorDispatchCall(writer, function, importName, plan, keepCount);
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void WriteConstructorDispatchCall(CodeWriter writer, FunctionBinding function, string importName,
        ManagedInvocationPlan plan, int keepCount)
    {
        var args = BuildManagedDispatchArguments(function, plan, [], keepCount);

        writer.Write($$"""
            var __handle = __PInvoke.{{importName}}({{args}});
            """);

        foreach (var statement in plan.PostCallStatements)
        {
            writer.Write(statement);
        }

        writer.Write(
            """
            return __handle;
            """);
    }

}
