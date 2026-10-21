using SharpCpp.Model.Ast;
using SharpCpp.Parsing;

namespace SharpCpp.Pipeline;

public static class DegradeUnsupportedSurfacePass
{
    /// <summary>
    /// Non-template members of a generic record dispatch per instantiation without
    /// native-default overloads, so a default C# cannot express (often dependent on a
    /// template argument) makes its parameter required.
    /// </summary>
    private static void RequireNativeDefaultsOfGenericRecordMembers(BindingCompilation compilation)
    {
        foreach (var parameter in compilation.Records
                     .Where(static record => record.IsGeneric)
                     .SelectMany(static record => record.Members.OfType<FunctionBinding>())
                     .Where(static function => !function.IsGeneric)
                     .SelectMany(static function => function.Parameters))
        {
            parameter.RemoveMetadata(MetadataKeys.UnexpressibleDefault);
            parameter.RemoveMetadata(MetadataKeys.UndefaultableParam);
        }
    }

    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ResolveReservedMemberNames(compilation);
        ResolveManagedFieldNameCollisions(compilation);
        RequireNativeDefaultsOfGenericRecordMembers(compilation);

        var changed = true;
        while (changed)
        {
            changed = false;
            var recordsById = compilation.Records
                .GroupBy(static record => record.SymbolId, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
            var enumsById = compilation.Enums
                .GroupBy(static @enum => @enum.SymbolId, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
            var allowedNamespaceRoots = compilation.NamespaceMappings.Keys.ToHashSet(StringComparer.Ordinal);
            HashSet<string>? referencedRecordIds = null;

            foreach (var duplicateGroup in compilation.Functions
                         .Where(static function => function.Parent is not RecordBinding && !function.IsIgnored() && function.Parent?.IsIgnored() != true)
                         .GroupBy(GetCallableSignatureKey, StringComparer.Ordinal)
                         .Where(static group => group.Count() > 1))
            {
                var declarations = duplicateGroup
                    .OrderByDescending(static function => function.CppQualifiedName.Count(static character => character == ':'))
                    .ThenBy(static function => function.SymbolId, StringComparer.Ordinal)
                    .ToArray();
                declarations[0].SetWarning($"selected canonical declaration from {declarations.Length} equivalent callable declarations");
                foreach (var function in declarations.Skip(1))
                {
                    function.SetSilentExclusionReason($"callable signature '{duplicateGroup.Key}' duplicates an equivalent declaration");
                    changed = true;
                }
            }

            foreach (var duplicateGroup in compilation.Functions
                          .Where(static function => function.Parent is not RecordBinding && !function.IsIgnored() && function.Parent?.IsIgnored() != true)
                          .GroupBy(GetManagedCallableSignatureKey, StringComparer.Ordinal)
                          .Where(static group => group.Count() > 1))
            {
                var declarations = duplicateGroup
                    .OrderByDescending(static function => function.CppQualifiedName.Count(static character => character == ':'))
                    .ThenBy(static function => function.SymbolId, StringComparer.Ordinal)
                    .ToArray();
                declarations[0].SetWarning($"selected canonical declaration from {declarations.Length} equivalent managed declarations");
                foreach (var function in declarations.Skip(1))
                {
                    function.SetSilentExclusionReason($"managed callable signature '{duplicateGroup.Key}' duplicates an equivalent managed declaration");
                    changed = true;
                }
            }

            foreach (var duplicateDelegateGroup in EnumerateCallbackDelegateParameters(compilation)
                         .GroupBy(GetCallbackDelegatePolicyTypeName, StringComparer.Ordinal)
                         .Where(static group => group.Select(parameter => GetCallbackSignatureKey(parameter.Type)).Distinct(StringComparer.Ordinal).Count() > 1))
            {
                foreach (var parameter in duplicateDelegateGroup)
                {
                    if (parameter.Parent is FunctionBinding function && !function.IsIgnored())
                    {
                        function.SetIgnoreReason($"callback delegate type '{duplicateDelegateGroup.Key}' is requested with incompatible callback signatures");
                        changed = true;
                    }
                }
            }

            foreach (var record in compilation.Records)
            {
                // An explicit interface implementation shares its signature with the hiding member by design.
                var duplicateGroups = record.Members.OfType<FunctionBinding>()
                    .Where(static function => !function.IsIgnored() && !function.HasMetadata(MetadataKeys.ExplicitInterfaceImplementation))
                    .GroupBy(GetManagedPublicCallableSignatureKey, StringComparer.Ordinal)
                    .Where(static group => group.Count() > 1)
                    .ToArray();

                foreach (var duplicateGroup in duplicateGroups)
                {
                    var kept = duplicateGroup
                        .OrderBy(static function => function.IsConstMethod)
                        .ThenBy(static function => function.SymbolId, StringComparer.Ordinal)
                        .First();

                    foreach (var function in duplicateGroup.Where(function => !ReferenceEquals(function, kept)))
                    {
                        function.SetIgnoreReason($"managed callable signature '{duplicateGroup.Key}' is declared multiple times in this bootstrap surface");
                        changed = true;
                    }
                }
            }

            foreach (var record in compilation.Records)
            {
                if (record.IsIgnored())
                {
                    continue;
                }

                if (record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
                {
                    continue;
                }

                if (!IsMarshallerBackedWrapperSurface(record) &&
                    TryGetNamespaceExclusionReason(record.CppQualifiedName, allowedNamespaceRoots, out var namespaceReason))
                {
                    record.SetIgnoreReason(namespaceReason);
                    changed = true;
                    continue;
                }

                // A marshaller-backed record needs no complete definition: the marshaller owns its surface.
                if (record.GetMetadata<bool>(MetadataKeys.ForwardDeclaration) &&
                    !record.GetMetadata<bool>(MetadataKeys.OpaqueNativeHandle) &&
                    !IsMarshallerBackedWrapperSurface(record))
                {
                    record.SetIgnoreReason("record is only forward-declared; wrapper materialization requires a complete definition");
                    changed = true;
                    continue;
                }

                referencedRecordIds ??= CollectReferencedRecordIds(compilation);
                if (TryGetUnsupportedRecordReason(record, compilation, recordsById, referencedRecordIds, out var recordReason))
                {
                    record.SetIgnoreReason(recordReason);
                    changed = true;
                }
            }

            foreach (var @enum in compilation.Enums)
            {
                if (@enum.IsIgnored())
                {
                    continue;
                }

                if (@enum.GetMetadata<bool>(MetadataKeys.SuppressEmission))
                {
                    continue;
                }

                if (TryGetNamespaceExclusionReason(@enum.ScopeCppQualifiedName, allowedNamespaceRoots, out var namespaceReason))
                {
                    @enum.SetIgnoreReason(namespaceReason);
                    changed = true;
                }
            }

            foreach (var function in compilation.Functions)
            {
                if (function.IsIgnored() || function.Parent?.IsIgnored() == true)
                {
                    continue;
                }

                if (!function.HasMetadata(MetadataKeys.MarshallerExposedMethod) &&
                    TryGetNamespaceExclusionReason(function.CppQualifiedName, allowedNamespaceRoots, out var namespaceReason))
                {
                    function.SetIgnoreReason(namespaceReason);
                    changed = true;
                    continue;
                }

                if (TryGetUnsupportedFunctionReason(function, recordsById, enumsById, compilation, out var reason))
                {
                    function.SetIgnoreReason(reason);
                    changed = true;
                }
            }

            foreach (var field in compilation.Descendants<FieldBinding>())
            {
                if (field.IsIgnored() || field.Parent?.IsIgnored() == true)
                {
                    continue;
                }

                if (TryGetUnsupportedFieldReason(field, recordsById, enumsById, out var reason))
                {
                    field.SetIgnoreReason(reason);
                    changed = true;
                }
            }

            foreach (var record in compilation.Records)
            {
                if (!record.IsNonCopyable && record.Members.OfType<FieldBinding>().Any(field => HoldsNonCopyableValue(field, recordsById)))
                {
                    record.IsNonCopyable = true;
                    record.HasCopyConstructor = false;
                    changed = true;
                }
            }
        }
    }

    /// <summary>
    /// Managed names of the public and protected <c>SafeHandle</c> and <c>NativeObject</c> instance surface. A
    /// generated member with one of these names would hide (CS0108) or be confused with the wrapper's own
    /// lifetime API, whatever its parameters. <c>object</c> members are not listed; they have their own
    /// <c>new</c> handling.
    /// </summary>
    private static readonly IReadOnlySet<string> ReservedInstanceMemberNames = new HashSet<string>(StringComparer.Ordinal)
    {
        // SafeHandle
        "Close", "Dispose", "DangerousGetHandle", "DangerousAddRef", "DangerousRelease",
        "SetHandleAsInvalid", "SetHandle", "ReleaseHandle", "IsClosed", "IsInvalid",
        // NativeObject
        "OwnsNativeHandle", "ManagesInstanceCacheLifecycle", "InstanceCache",
        "IsManagedOverride", "ReleaseNativeHandle", "PrepareForOwnedRegistration", "TryPromoteToOwningHandle",
        "AcquireOwningHandleCore", "DisposeFromNativeRuntimeCore",
    };

    /// <summary>
    /// The static <c>NativeObject</c> helpers with their parameter types (<c>GetObject(object?)</c>,
    /// <c>GetHandle(object?)</c> and <c>TransferHandleOwnership(object?)</c>). Only a generated function with the same name and
    /// the same by-value parameter types has the same signature and would hide the helper; any other signature is
    /// a plain overload and keeps its name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> ReservedStaticHelperSignatures =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["GetObject"] = ["object"],
            ["GetHandle"] = ["object"],
            ["TransferHandleOwnership"] = ["object"],
        };

    /// <summary>
    /// Whether <paramref name="function"/> would hide or be confused with the <c>SafeHandle</c>/<c>NativeObject</c>
    /// surface: an instance name at any arity, or a static helper name with exactly the helper's signature.
    /// </summary>
    internal static bool IsReservedMember(FunctionBinding function)
    {
        var name = function.ManagedName;
        if (ReservedInstanceMemberNames.Contains(name))
            return true;

        return ReservedStaticHelperSignatures.TryGetValue(name, out var signature) &&
               function.Parameters.Count == signature.Length &&
               function.Parameters.Zip(signature).All(static pair =>
                   pair.First.Direction is BindingParameterDirection.In &&
                   NormalizeManagedTypeName(pair.First.Type.ManagedTypeName) == pair.Second);
    }

    private static string NormalizeManagedTypeName(string managedTypeName)
    {
        var name = managedTypeName.Trim().Replace("?", string.Empty, StringComparison.Ordinal);
        if (name.StartsWith("global::", StringComparison.Ordinal))
            name = name["global::".Length..];
        if (name.StartsWith("System.", StringComparison.Ordinal))
            name = name["System.".Length..];
        return name switch { "Object" => "object", "String" => "string", _ => name };
    }

    /// <summary>
    /// Whether <paramref name="managedName"/> is reserved whatever the signature. A property hides an inherited
    /// method of the same name, so properties use this check.
    /// </summary>
    internal static bool IsReservedPropertyName(string managedName) =>
        ReservedInstanceMemberNames.Contains(managedName) || ReservedStaticHelperSignatures.ContainsKey(managedName);

    /// <summary>
    /// Renames bound functions that would hide the <c>SafeHandle</c>/<c>NativeObject</c> surface (see
    /// <see cref="IsReservedMember"/>) to <c>{Name}{ClassName}</c>, e.g. <c>Close</c> on <c>FileInterface</c>
    /// becomes <c>CloseFileInterface</c>. <c>ClassName</c> is the simple managed name of the owning record
    /// (see <see cref="GetSimpleRecordName"/>); for a virtual function it is the record that first declares the
    /// virtual (the root of its override chain, see <see cref="FindVirtualRoot"/>), so every override of the
    /// chain shares one name and the managed <c>override</c> keeps lining up.
    /// Functions with an explicit rename (<see cref="MetadataKeys.ExplicitManagedRename"/>) keep their name.
    /// The reserved functions of a record are grouped by their old name and the whole group (an overload set) gets
    /// one replacement. The replacement is taken only if a member outside the group already uses it: a non-virtual
    /// group then gets a counter suffix, while a group with a virtual function is ignored instead, because its
    /// replacement is fixed by the override chain. A group whose virtual functions have different roots is
    /// ignored too, since one name cannot satisfy both chains.
    /// </summary>
    private static void ResolveReservedMemberNames(BindingCompilation compilation)
    {
        Dictionary<string, FunctionBinding>? functionsByDeclarationId = null;

        foreach (var record in compilation.Records)
        {
            var groups = record.Members
                .OfType<FunctionBinding>()
                .Where(static function => !function.IsIgnored() &&
                                          !function.IsConstructor &&
                                          IsReservedMember(function) &&
                                          !function.GetMetadata<bool>(MetadataKeys.ExplicitManagedRename))
                .GroupBy(static function => function.ManagedName, StringComparer.Ordinal)
                .ToArray();
            if (groups.Length == 0)
                continue;

            var usedNames = record.Members
                .OfType<BindingNamedNode>()
                .Where(static member => !member.IsIgnored())
                .Select(static member => member.ManagedName)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var group in groups)
            {
                var oldName = group.Key;
                var virtualFunctions = group.Where(static function => function.IsVirtual).ToArray();
                bool IsTaken(string name) => usedNames.Contains(name) || record.HasOwnManagedMemberNamed(name);

                string newName;
                if (virtualFunctions.Length > 0)
                {
                    functionsByDeclarationId ??= BuildFunctionsByDeclarationId(compilation);
                    var rootNames = virtualFunctions
                        .Select(function => GetSimpleRecordName(FindVirtualRoot(function, functionsByDeclarationId).FindOwningRecord() ?? record))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (rootNames.Length > 1)
                    {
                        foreach (var function in group)
                            function.SetIgnoreReason($"'{function.CppQualifiedName}' cannot be renamed because '{oldName}' would hide the SafeHandle/NativeObject member of the same name and its virtual overloads override virtuals declared in different records ({string.Join(", ", rootNames)})");
                        continue;
                    }

                    newName = oldName + rootNames[0];
                    if (IsTaken(newName))
                    {
                        foreach (var function in group)
                            function.SetIgnoreReason($"'{function.CppQualifiedName}' cannot be bound as '{newName}' because '{oldName}' would hide the SafeHandle/NativeObject member of the same name and '{newName}' is already taken in '{record.CppQualifiedName}'");
                        continue;
                    }
                }
                else
                {
                    var baseName = oldName + GetSimpleRecordName(record);
                    newName = baseName;
                    for (var index = 2; IsTaken(newName); ++index)
                        newName = baseName + index;
                }

                usedNames.Add(newName);
                foreach (var function in group)
                {
                    function.ManagedName = newName;
                    function.SetWarning($"'{function.CppQualifiedName}' is bound as '{newName}' because '{oldName}' would hide the SafeHandle/NativeObject member of the same name");
                }
            }
        }
    }

    private static Dictionary<string, FunctionBinding> BuildFunctionsByDeclarationId(BindingCompilation compilation)
    {
        var functionsByDeclarationId = new Dictionary<string, FunctionBinding>(StringComparer.Ordinal);
        foreach (var function in compilation.Records.SelectMany(static record => record.Members.OfType<FunctionBinding>()))
        {
            if (!string.IsNullOrWhiteSpace(function.NativeDeclarationId))
                functionsByDeclarationId.TryAdd(function.NativeDeclarationId, function);
        }

        return functionsByDeclarationId;
    }

    /// <summary>
    /// Follows <see cref="FunctionBinding.OverriddenNativeDeclarationIds"/> transitively to the function that first
    /// declares the virtual. With several overridden declarations (multiple inheritance) the first one that
    /// resolves is followed, so the primary-base chain wins. An overridden declaration that is not bound (for
    /// example from a record outside the compilation) ends the walk at the last bound function.
    /// </summary>
    private static FunctionBinding FindVirtualRoot(
        FunctionBinding function,
        IReadOnlyDictionary<string, FunctionBinding> functionsByDeclarationId)
    {
        var visited = new HashSet<FunctionBinding>(ReferenceEqualityComparer.Instance) { function };
        var current = function;
        while (true)
        {
            var next = current.OverriddenNativeDeclarationIds
                .Select(id => functionsByDeclarationId.GetValueOrDefault(id))
                .FirstOrDefault(candidate => candidate is not null && !visited.Contains(candidate));
            if (next is null)
                return current;

            visited.Add(next);
            current = next;
        }
    }

    /// <summary>
    /// The managed name of <paramref name="record"/> as a bare identifier: no namespace or enclosing type
    /// (text up to the last <c>.</c>) and no generic argument list.
    /// A record projected onto a type the generator does not emit (<c>suppress_emission</c> or a managed
    /// wrapper assembly, like <c>JPH::BodyID</c> as <c>uint</c>) has that external type as its
    /// <see cref="BindingNode.ManagedName"/>; the bare name the record would otherwise have is used instead.
    /// </summary>
    private static string GetSimpleRecordName(RecordBinding record)
    {
        var projectedOntoExternalType =
            (record.GetMetadata<bool>(MetadataKeys.SuppressEmission) ||
             record.HasMetadata(MetadataKeys.ManagedWrapperAssembly)) &&
            !record.GetMetadata<bool>(MetadataKeys.ExplicitManagedRename);
        var name = projectedOntoExternalType
            ? ClangSharpHeaderParser.GetDefaultRecordManagedName(record.NativeName)
            : record.ManagedName;
        var generic = name.IndexOfAny(['`', '<']);
        if (generic >= 0)
            name = name[..generic];
        return name[(name.LastIndexOf('.') + 1)..];
    }

    private static void ResolveManagedFieldNameCollisions(BindingCompilation compilation)
    {
        foreach (var record in compilation.Records)
        {
            var usedNames = record.Members
                .OfType<BindingNamedNode>()
                .Where(static member => member is not FieldBinding && !member.IsIgnored())
                .Select(static member => member.ManagedName)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var field in record.Members
                         .OfType<FieldBinding>()
                         .Where(static field => !field.IsIgnored())
                         .OrderBy(static field => field.IsStatic)
                         .ThenBy(static field => field.SymbolId, StringComparer.Ordinal))
            {
                if (usedNames.Add(field.ManagedName))
                    continue;

                var suffix = field.IsStatic
                    ? field.IsReadOnly ? "Constant" : "Static"
                    : "Value";
                var baseName = field.ManagedName + suffix;
                var managedName = baseName;
                for (var index = 2; !usedNames.Add(managedName); ++index)
                    managedName = baseName + index;
                field.ManagedName = managedName;
            }
        }
    }

    // The renderer applies throw_on_false only on the plain method path. The overload paths keep the bool result.
    private static bool TryGetThrowOnFalseReason(FunctionBinding function, out string reason)
    {
        if (function.ReturnType.PrimitiveKind != BindingPrimitiveKind.Boolean)
        {
            reason = "throw_on_false needs a bool return type";
            return true;
        }

        if (function.Parameters.Any(static parameter =>
                parameter.HasMetadata(MetadataKeys.UnexpressibleDefault) ||
                parameter.HasMetadata(MetadataKeys.UndefaultableParam)))
        {
            reason = "throw_on_false is not supported on a method with generated default-argument overloads";
            return true;
        }

        if (function.Parameters.Any(static parameter =>
                parameter.HasMetadata(MetadataKeys.InputPointerArray) ||
                parameter.Direction == BindingParameterDirection.In &&
                parameter.AbiKind == BindingParameterAbiKind.Pointer &&
                parameter.Type.Kind == BindingTypeKind.Handle))
        {
            reason = "throw_on_false is not supported on a method with a pointer array parameter";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool TryGetUnsupportedFunctionReason(
        FunctionBinding function,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        IReadOnlyDictionary<string, EnumBinding> enumsById,
        BindingCompilation compilation,
        out string reason)
    {
        if (function.IsConstructor &&
            function.Parent is RecordBinding { IsAbstract: true } abstractRecord &&
            !abstractRecord.RequiresDirector(compilation))
        {
            reason = $"constructors on abstract record '{abstractRecord.CppQualifiedName}' need a director with at least one bindable virtual slot";
            return true;
        }

        if (function.HasMetadata(MetadataKeys.MarshallerThrowOnFalse) && TryGetThrowOnFalseReason(function, out reason))
        {
            return true;
        }

        var functionAllowsOpenGenericTypes = function.IsGeneric || (function.Parent as RecordBinding)?.IsGeneric == true;

        if (!functionAllowsOpenGenericTypes && ContainsOpenGenericParameter(function.ReturnType))
        {
            reason = $"return type is unsupported in this bootstrap surface: open generic parameter usage is not renderer-ready in a non-generic callable";
            return true;
        }

        if (function.ReturnType.IsCallback)
        {
            reason = "callback return types are not yet supported in this bootstrap surface";
            return true;
        }

        // A direct call can return a span view of native memory. A virtual return needs the
        // managed override to give native code a span, which the director cannot do.
        if (function.ReturnType.Marshaller.IsSpanProjection &&
            (!function.ReturnType.Marshaller.IsSpanViewReturn || function.IsVirtual))
        {
            reason = $"return type is unsupported in this bootstrap surface: {SpanProjectionReason(function.ReturnType, "a return value")}";
            return true;
        }

        if (TryGetUnsupportedTypeReason(function.ReturnType, function.CppReturnTypeName, recordsById, enumsById, disallowNonPointerHandles: false, out reason))
        {
            reason = $"return type is unsupported in this bootstrap surface: {reason}";
            return true;
        }

        foreach (var parameter in function.Parameters)
        {
            if (parameter.Type.ManagedMarshaller is not null &&
                parameter.Direction is BindingParameterDirection.Ref or BindingParameterDirection.Out)
            {
                reason = $"parameter '{parameter.ManagedName}' is unsupported in this bootstrap surface: custom marshaller-backed types do not support mutable by-reference parameters";
                return true;
            }

            if (!functionAllowsOpenGenericTypes && parameter.Type.ContainsGenericParameters)
            {
                reason = $"parameter '{parameter.ManagedName}' is unsupported in this bootstrap surface: open generic parameter usage is not renderer-ready in a non-generic callable";
                return true;
            }

            if (parameter.Type.IsCallback &&
                (parameter.Direction is not BindingParameterDirection.In || parameter.AbiKind is not BindingParameterAbiKind.Value))
            {
                reason = $"parameter '{parameter.ManagedName}' is unsupported in this bootstrap surface: callback parameters must be passed as in-value parameters";
                return true;
            }

            if (function.IsVirtual && parameter.Type.Marshaller.IsSpanProjection)
            {
                reason = $"parameter '{parameter.ManagedName}' is unsupported in this bootstrap surface: {SpanProjectionReason(parameter.Type, "a virtual method parameter")}";
                return true;
            }

            if (TryGetUnsupportedTypeReason(parameter.Type, parameter.CppTypeName, recordsById, enumsById, disallowNonPointerHandles: false, out reason))
            {
                reason = $"parameter '{parameter.ManagedName}' is unsupported in this bootstrap surface: {reason}";
                return true;
            }
        }

        if (function.IsGeneric &&
            !function.ReturnType.ContainsGenericParameters &&
            function.Parameters.All(static parameter => !parameter.Type.ContainsGenericParameters))
        {
            reason = $"generic function '{function.ManagedName}' does not expose its template parameters through supported ABI positions";
            return true;
        }

        if (!function.IsConstructor &&
            function.NativeName.StartsWith("operator", StringComparison.Ordinal) &&
            function.OperatorToken is null &&
            !function.IsConversionOperator &&
            !function.GetMetadata<bool>(MetadataKeys.ExplicitManagedRename))
        {
            reason = $"operator '{function.NativeName}' is not supported in this bootstrap surface";
            return true;
        }

        if (function.OperatorToken is not null && !IsSupportedOperatorShape(function))
        {
            reason = $"operator shape '{function.ManagedName}' is not supported in this bootstrap surface";
            return true;
        }

        if (function.Parent is RecordBinding owningRecord &&
            function.OperatorToken == "!=" &&
            !HasCompanionOperator(owningRecord, function, "=="))
        {
            reason = $"operator '{function.ManagedName}' does not have the companion comparison operator required by C#";
            return true;
        }

        if (function.Parent is RecordBinding owningRecord2 &&
            function.OperatorToken == "==" &&
            !IsSelfComparisonOperator(owningRecord2, function) &&
            !HasCompanionOperator(owningRecord2, function, "!="))
        {
            reason = $"operator '{function.ManagedName}' does not have the companion comparison operator required by C#";
            return true;
        }

        if (function.Parent is RecordBinding directorRecord &&
            directorRecord.IsDirector &&
            function.IsVirtual &&
            TryGetUnsupportedDirectorCallbackReason(function, out var callbackReason))
        {
            reason = $"director callback '{function.ManagedName}' is unsupported in this bootstrap surface: {callbackReason}";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static string SpanProjectionReason(BindingType type, string position) =>
        $"span projection '{type.ManagedTypeName}' can only be used for a parameter of a direct call, not {position}";

    private static bool TryGetUnsupportedDirectorCallbackReason(FunctionBinding function, out string reason)
    {
        foreach (var parameter in function.Parameters)
        {
            // Pointer ranges cross as raw pointers and are rebuilt as a span in the dispatch; the
            // derived end parameter is hidden from the public override signature.
            if (parameter.HasMetadata(MetadataKeys.DerivedArgument) ||
                parameter.GetPointerRangeBeginKind() != PointerRangeBeginKind.None)
            {
                continue;
            }

            if (parameter.HasMetadata(MetadataKeys.EndedBy))
            {
                reason = $"parameter '{parameter.ManagedName}' is a pointer range without a span projection; it cannot be bound in a director callback";
                return true;
            }

            // A counted string array is decoded into a span of managed strings sized by its count
            // parameter, which stays an ordinary in-value parameter; without a count it cannot be.
            if (parameter.IsDecodedInputStringArray())
            {
                if (parameter.TryGetCountedStringArrayCount(function, out _))
                    continue;

                reason = $"parameter '{parameter.ManagedName}' is a string array without a count parameter; tag it with [[sharpcpp::counted_by(...)]] to bind it in a director callback";
                return true;
            }

            if (parameter.Direction is not BindingParameterDirection.In || parameter.AbiKind is not BindingParameterAbiKind.Value)
            {
                reason = $"parameter '{parameter.ManagedName}' must be an in-value parameter";
                return true;
            }

            // A director gives a native function object to a managed override as a delegate
            // (borrowed for a const reference, owned for a sink). Only some callback shapes can cross.
            if (parameter.Type.IsCallback &&
                TryGetUnsupportedDirectorNativeCallbackReason(parameter.Type, out var callbackShapeReason))
            {
                reason = $"parameter '{parameter.ManagedName}' is a callback that a director cannot give to a managed override: {callbackShapeReason}";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    // The managed delegate over a native function object converts its arguments and its return
    // value with no allocation. A numeric primitive (not bool) can be a value, a const reference
    // or a mutable reference (a managed ref parameter). An enum can be a value.
    private static bool TryGetUnsupportedDirectorNativeCallbackReason(BindingType callback, out string reason)
    {
        if (callback.ManagedMarshaller is not null)
        {
            reason = $"the callback type '{callback.ManagedTypeName}' has a custom managed marshaller";
            return true;
        }

        var returnType = callback.CallbackReturnType ?? BindingType.Void;
        if (returnType.PrimitiveKind != BindingPrimitiveKind.Void &&
            (returnType.Marshaller.CppIsReference || !IsDirectorNativeCallbackValueSupported(returnType)))
        {
            reason = $"the callback return type '{returnType.ManagedTypeName}' is not void, a numeric primitive or an enum";
            return true;
        }

        foreach (var parameterType in callback.CallbackParameterTypes)
        {
            if (!IsDirectorNativeCallbackValueSupported(parameterType) ||
                (parameterType.Marshaller.CppIsReference && !parameterType.IsCallbackValueReference))
            {
                reason = $"the callback parameter type '{parameterType.ManagedTypeName}' is not a numeric primitive, a reference to a numeric primitive or an enum";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsDirectorNativeCallbackValueSupported(BindingType type) =>
        string.IsNullOrWhiteSpace(type.MarshallerTypeName) &&
        !type.IsCallback &&
        !type.IsPointerType &&
        !type.Marshaller.IsRefStruct &&
        type.Kind switch
        {
            BindingTypeKind.Primitive => type.PrimitiveKind is not (BindingPrimitiveKind.Unsupported or BindingPrimitiveKind.Void or BindingPrimitiveKind.Boolean),
            BindingTypeKind.Enum => true,
            _ => false
        };

    private static HashSet<string> CollectReferencedRecordIds(BindingCompilation compilation)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in compilation.Functions.Where(static function => !function.IsIgnored() && function.Parent?.IsIgnored() != true))
        {
            AddReferencedRecordIds(function.ReturnType, referenced);
            foreach (var parameter in function.Parameters)
                AddReferencedRecordIds(parameter.Type, referenced);
        }

        foreach (var field in compilation.Records
                     .Where(static record => !record.IsIgnored())
                     .SelectMany(static record => record.Members.OfType<FieldBinding>())
                     .Where(static field => !field.IsIgnored()))
            AddReferencedRecordIds(field.Type, referenced);
        return referenced;
    }

    private static void AddReferencedRecordIds(BindingType type, HashSet<string> referenced)
    {
        if (type.RecordId is not null)
            referenced.Add(type.RecordId);
        foreach (var argument in type.GenericArguments)
            AddReferencedRecordIds(argument, referenced);
    }

    private static bool TryGetUnsupportedRecordReason(
        RecordBinding record,
        BindingCompilation compilation,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        IReadOnlySet<string> referencedRecordIds,
        out string reason)
    {
        if (record.IsDirector)
        {
            foreach (var method in record.Members.OfType<FunctionBinding>().Where(static method => method.IsPureVirtual))
            {
                if (method.IsIgnored())
                {
                    reason = $"pure virtual director callback '{method.ManagedName}' is unavailable in this bootstrap surface: {method.GetIgnoreReason()}";
                    return true;
                }

                if (TryGetUnsupportedDirectorCallbackReason(method, out var callbackReason))
                {
                    reason = $"pure virtual director callback '{method.ManagedName}' is unsupported in this bootstrap surface: {callbackReason}";
                    return true;
                }
            }
        }

        // Records with no user-defined members are template utility types
        // (metafunctions like IndexInTuple) that shouldn't be wrapped.
        // Their only members would be the synthetic wrapper boilerplate (Handle,
        // constructors, P/Invoke) which is never useful without API methods.
        // Exceptions: records with a managed base type or type format are
        // configured by marshallers (e.g. std::string → StringBase), and a declared
        // record a bound signature names (a tag type used as a template argument) is a
        // type the surface needs.
        if (string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) &&
            (string.IsNullOrEmpty(record.HeaderIncludePath) || !referencedRecordIds.Contains(record.SymbolId)) &&
            (record.ManagedTypeFormat is null || record.ManagedTypeFormat == record.ManagedName) &&
            !record.GetMetadata<bool>(MetadataKeys.OpaqueNativeHandle) &&
            !record.Members.OfType<FunctionBinding>().Any(static m => !m.IsConstructor && !m.HasMetadata(MetadataKeys.IgnoreReason)) &&
            !record.Members.OfType<FieldBinding>().Any() &&
            !record.Members.OfType<FunctionBinding>().Any(static m => m.IsConstructor && !m.HasMetadata(MetadataKeys.IgnoreReason)))
        {
            reason = $"record '{record.CppQualifiedName}' has no user-defined API surface (it is likely a template utility type)";
            return true;
        }

        var unavailableBases = new List<RecordBaseBinding>();

        foreach (var baseBinding in record.Bases)
        {
            if (!recordsById.TryGetValue(baseBinding.RecordId, out var baseRecord) ||
                baseRecord.IsIgnored() ||
                baseRecord.DerivesFromTemplateParameter ||
                (baseRecord.GetMetadata<bool>(MetadataKeys.SuppressEmission) && !IsMarshallerBackedWrapperSurface(baseRecord)))
            {
                unavailableBases.Add(baseBinding);
            }
        }

        foreach (var baseBinding in unavailableBases)
        {
            record.SetWarning($"dropping base '{baseBinding.CppQualifiedName}' from record '{record.CppQualifiedName}' because the base record is not available for a coherent managed hierarchy; the derived record will still be generated without that base");
            var baseIndex = record.Bases.IndexOf(baseBinding);
            record.Bases.Remove(baseBinding);
            if (!baseBinding.IsVirtual && recordsById.TryGetValue(baseBinding.RecordId, out var droppedBaseRecord))
            {
                // Recorded at parse time: the template's own parameter base may already have
                // been dropped from its base list by this pass.
                if (droppedBaseRecord.TemplateParameterBaseName is { } parameterBaseName)
                {
                    var parameterBase = SubstituteHoistedBase(
                        new RecordBaseBinding(droppedBaseRecord.SymbolId, parameterBaseName, parameterBaseName, parameterBaseName, isVirtual: false, BindingType.CreateGenericParameter(parameterBaseName)),
                        droppedBaseRecord,
                        baseBinding,
                        recordsById);
                    if (parameterBase is null)
                    {
                        WarnLostHoistedBase(record, baseBinding, parameterBaseName, "it does not resolve to a concrete record");
                    }
                    else if (!record.Bases.Any(existing => string.Equals(existing.RecordId, parameterBase.RecordId, StringComparison.Ordinal)))
                    {
                        record.Bases.Insert(baseIndex++, parameterBase);
                    }
                }

                // Keep the inherited surface: the dropped base's own concrete bases are
                // still bases of this record in C++ (e.g. a variadic template's base).
                foreach (var candidate in droppedBaseRecord.Bases)
                {
                    if (candidate.IsVirtual)
                    {
                        WarnLostHoistedBase(record, baseBinding, candidate.CppQualifiedName, "it is a virtual base");
                        continue;
                    }

                    var hoisted = SubstituteHoistedBase(candidate, droppedBaseRecord, baseBinding, recordsById);
                    if (hoisted is null)
                    {
                        WarnLostHoistedBase(record, baseBinding, candidate.CppQualifiedName, "it does not resolve to a concrete record");
                    }
                    else if (!record.Bases.Any(existing => string.Equals(existing.RecordId, hoisted.RecordId, StringComparison.Ordinal)))
                    {
                        record.Bases.Insert(baseIndex++, hoisted);
                    }
                }
            }

            // A dependent base leaves no managed base name; the hoisted bases then provide it.
            if (string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) ||
                string.Equals(record.ManagedBaseTypeName, baseBinding.ManagedName, StringComparison.Ordinal))
            {
                record.ManagedBaseTypeName = null;
                // Re-pick managed base from remaining non-virtual, non-ignored bases
                foreach (var remainingBase in record.Bases)
                {
                    if (!remainingBase.IsVirtual &&
                        recordsById.TryGetValue(remainingBase.RecordId, out var remainingBaseRecord) &&
                        !remainingBaseRecord.IsIgnored())
                    {
                        record.ManagedBaseTypeName = remainingBase.ManagedName;
                        break;
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) &&
            !record.ManagedBaseTypeName.Contains('.', StringComparison.Ordinal) &&
            !HasAvailableNativeBaseRecord(compilation, record))
        {
            record.SetWarning($"dropping managed base type '{record.ManagedBaseTypeName}' from record '{record.ManagedName}' because the managed base type is not available for a coherent managed hierarchy; the derived record will still be generated without that base");
            record.ManagedBaseTypeName = null;
        }

        if (record.GetSecondaryBaseRecords(compilation).Any(static baseRecord =>
                baseRecord.IsIgnored() ||
                baseRecord.Members.OfType<FunctionBinding>().Any(static function =>
                    function.OperatorToken is not null ||
                    function.IsConversionOperator)))
        {
            reason = "secondary-base interfaces with comparison operators are not yet supported coherently";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static void WarnLostHoistedBase(RecordBinding record, RecordBaseBinding droppedBase, string lostBaseName, string cause)
    {
        var warning = $"base '{lostBaseName}' of the dropped base '{droppedBase.CppQualifiedName}' is not inherited by record '{record.CppQualifiedName}' because {cause}";
        if (!record.GetWarnings().Contains(warning))
        {
            record.SetWarning(warning);
        }
    }

    private static bool HasAvailableNativeBaseRecord(BindingCompilation compilation, RecordBinding record)
    {
        var primaryBase = record.Bases.FirstOrDefault(static candidate => !candidate.IsVirtual);
        return primaryBase is not null &&
            compilation.FindRecordBySymbolId(primaryBase.RecordId) is { } baseRecord &&
            !baseRecord.IsIgnored();
    }

    /// <summary>
    /// A base of a dropped generic base may be one of its template parameters
    /// (template &lt;class TBase&gt; struct tracked : TBase); resolve it to the concrete
    /// argument the derived record instantiated. Other open generic bases are unusable.
    /// </summary>
    private static RecordBaseBinding? SubstituteHoistedBase(
        RecordBaseBinding candidate,
        RecordBinding droppedBaseRecord,
        RecordBaseBinding droppedBase,
        IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        if (candidate.Type.IsGenericParameter)
        {
            var parameterIndex = droppedBaseRecord.GenericParameters
                .Select(static (parameter, index) => (parameter.Name, index))
                .FirstOrDefault(parameter => string.Equals(parameter.Name, candidate.Type.GenericParameterName, StringComparison.Ordinal))
                .index;
            if (parameterIndex >= droppedBase.Type.GenericArguments.Count ||
                droppedBase.Type.GenericArguments[parameterIndex] is not { RecordId: { } argumentRecordId } argument ||
                !recordsById.TryGetValue(argumentRecordId, out var argumentRecord) ||
                !droppedBaseRecord.GenericParameters.Any(parameter => string.Equals(parameter.Name, candidate.Type.GenericParameterName, StringComparison.Ordinal)))
            {
                return null;
            }

            return new RecordBaseBinding(
                argumentRecord.SymbolId,
                argumentRecord.NativeName,
                argument.ManagedTypeName,
                argumentRecord.CppQualifiedName,
                isVirtual: false,
                argument);
        }

        return candidate.Type.ContainsGenericParameters ? null : candidate;
    }

    private static bool TryGetUnsupportedFieldReason(
        FieldBinding field,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        IReadOnlyDictionary<string, EnumBinding> enumsById,
        out string reason)
    {
        if (field.Parent is RecordBinding { IsGeneric: true } genericRecord &&
            !IsGenericRecordFieldShapeSupported(genericRecord, field))
        {
            reason = "fields on generic records are only bound as public, non-static, non-reference, non-pointer, non-array, non-bitfield value members";
            return true;
        }

        if (field.Parent is RecordBinding { IsGeneric: true } && field.Type.ContainsDependentTemplateSpelling)
        {
            reason = "field type nests the record's template parameter inside another template that the parser left as an unresolved dependent type; its per-instantiation shape cannot be collected, so the field is not bound";
            return true;
        }

        if (field.Parent is RecordBinding { IsGeneric: true } specializedRecord &&
            specializedRecord.GetMetadata<string>(MetadataKeys.ExplicitFullSpecializations) is { Length: > 0 } specializations)
        {
            reason = $"the class template has explicit full specializations ({specializations}) whose members are independent of the primary template; primary-template field accessors would not match those instantiations, so the field is not bound";
            return true;
        }

        if ((field.Parent as RecordBinding)?.IsGeneric != true && field.Type.ContainsGenericParameters)
        {
            reason = "field type is unsupported in this bootstrap surface: open generic parameter usage is not renderer-ready in a non-generic record";
            return true;
        }

        if (field.IsReference)
        {
            reason = "reference fields are not yet supported coherently";
            return true;
        }

        if (field.Type.IsCallback)
        {
            reason = "callback fields are not yet supported in this bootstrap surface";
            return true;
        }

        if (field.Type.Marshaller.IsSpanProjection)
        {
            reason = $"field type is unsupported in this bootstrap surface: {SpanProjectionReason(field.Type, "a field")}";
            return true;
        }

        if (TryGetUnsupportedTypeReason(field.Type, field.CppTypeName, recordsById, enumsById, disallowNonPointerHandles: true, out reason))
        {
            reason = $"field type is unsupported in this bootstrap surface: {reason}";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    /// <summary>
    /// Generic-record fields are rendered per instantiation through plain member access,
    /// so only the simple value shape is supported; everything else keeps the record-level degrade.
    /// </summary>
    private static bool IsGenericRecordFieldShapeSupported(RecordBinding record, FieldBinding field) =>
        !field.IsStatic &&
        !field.IsReference &&
        !field.IsPointer &&
        !field.Type.IsFixedArray &&
        field.AccessModifier == BindingAccessModifier.Public &&
        !field.HasMetadata(MetadataKeys.BitField) &&
        !field.HasMetadata(MetadataKeys.NativeFieldAccessExpression) &&
        !field.HasMetadata(MetadataKeys.FunctionPointerField) &&
        !record.HasMetadata(MetadataKeys.ManagedWrapperAssembly);

    /// <summary>
    /// Container marshallers expose members that take or return their element by value.
    /// Binding a non-copyable record as that element cannot compile natively.
    /// </summary>
    private static bool TryGetCopiedNonCopyableArgument(
        BindingType type,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        out string argumentName) =>
        TryGetElementCopiedByValue(type, recordsById, static record => record.IsNonCopyable, out argumentName);

    /// <summary>
    /// Sequence containers also assign elements (set, insert, erase); a record whose copy
    /// assignment is deleted cannot be bound as that element even when it is copyable.
    /// </summary>
    private static bool TryGetAssignedNonAssignableArgument(
        BindingType type,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        out string argumentName) =>
        TryGetElementCopiedByValue(type, recordsById, static record => record.IsNonAssignable, out argumentName);

    /// <summary>
    /// The generated wrapper of every container instantiation default-constructs the container,
    /// which needs a default-constructible allocator.
    /// </summary>
    private static bool TryGetNonDefaultConstructibleAllocator(BindingType type, out string allocatorName)
    {
        allocatorName = string.Empty;
        foreach (var argument in type.NativeTemplateArguments)
        {
            if (argument.Kind == BindingTemplateArgumentKind.Type && argument.IsNonDefaultConstructibleAllocator)
            {
                allocatorName = argument.Type!.CppTypeName;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetElementCopiedByValue(
        BindingType type,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        Func<RecordBinding, bool> isUnsuitable,
        out string argumentName)
    {
        argumentName = string.Empty;
        if (type.RecordId is null ||
            !recordsById.TryGetValue(type.RecordId, out var container) ||
            container.GenericParameters.Count < type.GenericArguments.Count)
        {
            return false;
        }

        for (var index = 0; index < type.GenericArguments.Count; ++index)
        {
            var argument = type.GenericArguments[index];
            if (argument.RecordId is null ||
                !argument.Marshaller.CppIsRecordValue ||
                !recordsById.TryGetValue(argument.RecordId, out var argumentRecord) ||
                !isUnsuitable(argumentRecord))
            {
                continue;
            }

            var parameterName = container.GenericParameters[index].Name;
            bool IsByValue(BindingType candidate) =>
                candidate.IsGenericParameter && string.Equals(candidate.GenericParameterName, parameterName, StringComparison.Ordinal);

            if (container.Members.OfType<FunctionBinding>().Any(function =>
                    IsByValue(function.ReturnType) || function.Parameters.Any(parameter => IsByValue(parameter.Type))))
            {
                argumentName = argumentRecord.CppQualifiedName;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The implicit copy constructor of a record is declared even when a member container
    /// copies non-copyable elements; instantiating it (clone, container copies) cannot compile.
    /// </summary>
    private static bool HoldsNonCopyableValue(FieldBinding field, IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        if (field.IsStatic || field.IsPointer || field.IsReference)
        {
            return false;
        }

        if (TryGetCopiedNonCopyableArgument(field.Type, recordsById, out _))
        {
            return true;
        }

        return field.Type.GenericArguments.Count == 0 &&
            field.Type.RecordId is not null &&
            recordsById.TryGetValue(field.Type.RecordId, out var fieldRecord) &&
            fieldRecord.IsNonCopyable;
    }

    private static bool TryGetUnsupportedTypeReason(
        BindingType type,
        string rawCppTypeName,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        IReadOnlyDictionary<string, EnumBinding> enumsById,
        bool disallowNonPointerHandles,
        out string reason)
    {
        if (type.IsCallback)
        {
            if (type.CallbackReturnType is null)
            {
                reason = $"callback type '{rawCppTypeName}' is missing return-type metadata";
                return true;
            }

            if (type.ManagedMarshaller is null && !IsGeneratedCallbackValueSupported(type.CallbackReturnType, recordsById))
            {
                reason = $"generated callback marshaller cannot convert callback return type '{type.CallbackReturnType.ManagedTypeName}'";
                return true;
            }

            if (type.CallbackReturnType.Marshaller.IsSpanProjection)
            {
                reason = $"callback return type is unsupported: {SpanProjectionReason(type.CallbackReturnType, "a callback return value")}";
                return true;
            }

            if (TryGetUnsupportedTypeReason(type.CallbackReturnType, type.CallbackReturnType.CppTypeName, recordsById, enumsById, disallowNonPointerHandles: false, out var callbackReturnReason))
            {
                reason = $"callback return type is unsupported: {callbackReturnReason}";
                return true;
            }

            foreach (var parameterType in type.CallbackParameterTypes)
            {
                // A read-only view parameter is valid only during the call, so only a parameter
                // (and never a callback return value) can be one.
                if (type.ManagedMarshaller is null &&
                    !parameterType.IsCallbackReadOnlyViewParameter &&
                    !IsGeneratedCallbackValueSupported(parameterType, recordsById))
                {
                    reason = $"generated callback marshaller cannot convert callback parameter type '{parameterType.ManagedTypeName}'";
                    return true;
                }

                // A generated callback marshaller gives a read-only view to the delegate as a
                // ReadOnlySpan<T> that is valid only during the call. Other span projections
                // (a mutable Span<T>) still need a direct call.
                if (parameterType.Marshaller.IsSpanProjection &&
                    (type.ManagedMarshaller is not null || !parameterType.IsCallbackReadOnlyViewParameter))
                {
                    reason = $"callback parameter type '{parameterType.ManagedTypeName}' is unsupported: {SpanProjectionReason(parameterType, "a callback parameter")}";
                    return true;
                }

                if (TryGetUnsupportedTypeReason(parameterType, parameterType.CppTypeName, recordsById, enumsById, disallowNonPointerHandles: false, out var callbackParameterReason))
                {
                    reason = $"callback parameter type '{parameterType.ManagedTypeName}' is unsupported: {callbackParameterReason}";
                    return true;
                }
            }

            reason = string.Empty;
            return false;
        }

        if (type.Kind == BindingTypeKind.Handle &&
            !string.IsNullOrWhiteSpace(type.RecordId) &&
            recordsById.TryGetValue(type.RecordId, out var record) &&
            record.IsIgnored())
        {
            reason = $"record '{record.CppQualifiedName}' is ignored: {record.GetIgnoreReason() ?? "unsupported record surface"}";
            return true;
        }

        if (type.Kind == BindingTypeKind.Enum &&
            !string.IsNullOrWhiteSpace(type.EnumId) &&
            enumsById.TryGetValue(type.EnumId, out var enumBinding) &&
            enumBinding.IsIgnored())
        {
            reason = $"enum '{enumBinding.CppQualifiedName}' is ignored: {enumBinding.GetIgnoreReason() ?? "unsupported enum surface"}";
            return true;
        }

        if (type.Kind == BindingTypeKind.Enum &&
            (string.IsNullOrWhiteSpace(type.EnumId) || !enumsById.ContainsKey(type.EnumId)))
        {
            reason = $"external enum type '{rawCppTypeName}' is not owned by this compilation";
            return true;
        }

        if (type.Kind == BindingTypeKind.Handle &&
            !string.IsNullOrWhiteSpace(type.RecordId) &&
            !recordsById.ContainsKey(type.RecordId))
        {
            reason = $"external handle type '{rawCppTypeName}' is not owned by this compilation";
            return true;
        }

        if (type.Kind == BindingTypeKind.Primitive && type.PrimitiveKind == BindingPrimitiveKind.Unsupported)
        {
            if (string.Equals(type.ManagedTypeName, rawCppTypeName, StringComparison.Ordinal))
            {
                reason = $"configured marshaller-backed type '{rawCppTypeName}' is not yet supported";
                return true;
            }
        }

        if (TryGetCopiedNonCopyableArgument(type, recordsById, out var nonCopyableArgument))
        {
            reason = $"generic argument '{nonCopyableArgument}' is not copyable, but '{rawCppTypeName}' copies its elements";
            return true;
        }

        if (TryGetAssignedNonAssignableArgument(type, recordsById, out var nonAssignableArgument))
        {
            reason = $"generic argument '{nonAssignableArgument}' is not copy-assignable, but '{rawCppTypeName}' assigns its elements";
            return true;
        }

        if (TryGetNonDefaultConstructibleAllocator(type, out var allocatorName))
        {
            reason = $"allocator '{allocatorName}' is not default-constructible, but '{rawCppTypeName}' is default-constructed by generated wrappers";
            return true;
        }

        foreach (var argument in GetGenericArgumentsForValidation(type))
        {
            // A callback crosses only as a parameter, with a callback handle that the call controls.
            // A generic type (for example a container) gives its arguments as values or handles,
            // so a callback argument would cross as a handle that does not exist.
            if (argument.IsCallback)
            {
                reason = $"generic argument '{argument.ManagedTypeName}' is unsupported: a callback cannot be a generic argument (for example a container element)";
                return true;
            }

            if (TryGetUnsupportedTypeReason(argument, argument.CppTypeName, recordsById, enumsById, disallowNonPointerHandles: false, out var nestedReason))
            {
                reason = $"generic argument '{argument.ManagedTypeName}' is unsupported: {nestedReason}";
                return true;
            }
        }

        if (type.HasUnresolvedManagedNamespaceSeparators)
        {
            reason = $"managed type name '{type.ManagedTypeName}' is not yet renderer-ready C#";
            return true;
        }

        if (HasRefStructGenericArgument(type))
        {
            reason = $"managed type name '{type.ManagedTypeName}' contains a ref struct generic argument which is not yet supported in this bootstrap surface";
            return true;
        }

        if (type.GenericArguments.Count > 0 &&
            type.ManagedMarshaller is null &&
            !IsSupportedGenericHandleType(type, recordsById))
        {
            reason = $"template/container type '{rawCppTypeName}' is not yet supported";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static IEnumerable<ParameterBinding> EnumerateCallbackDelegateParameters(BindingCompilation compilation)
    {
        return compilation.Functions
            .Where(static function => !function.IsIgnored() && function.Parent?.IsIgnored() != true)
            .SelectMany(static function => function.Parameters)
            .Where(static parameter => parameter.UseCallbackDelegate && parameter.Type.IsCallback);
    }

    private static string GetCallbackDelegatePolicyTypeName(ParameterBinding parameter)
    {
        if (!string.IsNullOrWhiteSpace(parameter.CallbackDelegateTypeName))
        {
            return IsExternalCallbackDelegateTypeName(parameter.CallbackDelegateTypeName!)
                ? NormalizeExternalCallbackDelegateTypeName(parameter.CallbackDelegateTypeName!)
                : parameter.CallbackDelegateTypeName!;
        }

        return GetGeneratedCallbackDelegateName(parameter);
    }

    private static string GetGeneratedCallbackDelegateName(ParameterBinding parameter)
    {
        var function = parameter.Parent as FunctionBinding;
        var ownerName = function?.FindOwningRecord()?.ManagedName ?? "Global";
        var functionName = function?.ImportMethodName ?? function?.ManagedName ?? "Callback";
        return $"{SanitizeManagedToken(ownerName)}_{SanitizeManagedToken(functionName)}_{SanitizeManagedToken(parameter.ManagedName)}Callback";
    }

    // The renderer uses the same key to find delegate declarations that do not agree.
    private static string GetCallbackSignatureKey(BindingType type) => type.CallbackSignatureKey;

    private static bool IsGeneratedCallbackValueSupported(BindingType type, IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        if (type.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            return true;
        }

        return type.Kind switch
        {
            BindingTypeKind.Primitive => type.PrimitiveKind != BindingPrimitiveKind.Unsupported ||
                IsGeneratedCallbackConfiguredValueSupported(type),
            BindingTypeKind.Handle => !recordsById.TryGetValue(type.RecordId ?? string.Empty, out var record) || record.IsHandleType,
            BindingTypeKind.Enum => true,
            _ => false
        };
    }

    private static IEnumerable<BindingType> GetGenericArgumentsForValidation(BindingType type)
    {
        return type.IsFixedArray && type.GenericArguments.Count >= 2
            ? type.GenericArguments.Take(1)
            : type.GenericArguments;
    }

    private static bool IsGeneratedCallbackConfiguredValueSupported(BindingType type)
    {
        return !string.IsNullOrWhiteSpace(type.MarshallerTypeName) &&
            string.Equals(type.MarshallerTypeName, type.UnaliasedManagedTypeName, StringComparison.Ordinal);
    }

    private static bool IsExternalCallbackDelegateTypeName(string value)
    {
        return value.StartsWith("global::", StringComparison.Ordinal) ||
            value.Contains('.', StringComparison.Ordinal);
    }

    private static string NormalizeExternalCallbackDelegateTypeName(string value)
    {
        return value.StartsWith("global::", StringComparison.Ordinal)
            ? value
            : $"global::{value}";
    }

    private static string SanitizeManagedToken(string value)
    {
        value = value.Replace("?", "Nullable", StringComparison.Ordinal);
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        var token = builder.ToString().Trim('_');
        return token.Length == 0 ? "Callback" : token;
    }

    private static bool IsSupportedGenericHandleType(BindingType type, IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        if (type.Kind != BindingTypeKind.Handle ||
            string.IsNullOrWhiteSpace(type.RecordId) ||
            !recordsById.TryGetValue(type.RecordId, out var record))
        {
            return false;
        }

        // The fallback path handles template specializations of non-generic
        // records (e.g. std::vector<int> where the vector record isn't marked
        // generic).  A non-generic record with generic arguments whose C++ type
        // name doesn't match the record's qualified name is a template
        // instantiation and is accepted.
        return record.IsGeneric ||
            (type.GenericArguments.Count > 0 &&
             !record.IsGeneric &&
             !type.CppTypeName.StartsWith(record.CppQualifiedName, StringComparison.Ordinal));
    }

    private static bool IsSupportedOperatorShape(FunctionBinding function)
    {
        return function switch
        {
            { OperatorToken: "+", Parameters.Count: 0 } => true,
            { OperatorToken: "+", Parameters.Count: 1 } => true,
            { OperatorToken: "-", Parameters.Count: 0 } => true,
            { OperatorToken: "!", Parameters.Count: 0 } => true,
            { OperatorToken: "==" or "!=" or "<" or ">", Parameters.Count: 1 } => true,
            { IsConversionOperator: true, Parameters.Count: 0 } => true,
            _ => false
        };
    }

    private static bool HasCompanionOperator(RecordBinding record, FunctionBinding function, string companionToken)
    {
        return record.Members.OfType<FunctionBinding>().Any(candidate =>
            candidate.OperatorToken == companionToken &&
            candidate.Parameters.Count == function.Parameters.Count &&
            candidate.Parameters.Select(static parameter => parameter.CppTypeName)
                .SequenceEqual(function.Parameters.Select(static parameter => parameter.CppTypeName), StringComparer.Ordinal));
    }

    private static bool IsSelfComparisonOperator(RecordBinding record, FunctionBinding function)
    {
        if (function.Parameters.Count != 1)
        {
            return false;
        }

        var normalizedParameterType = NormalizeComparisonType(function.Parameters[0].CppTypeName);
        var normalizedRecordType = NormalizeComparisonType(record.CppQualifiedName);
        return string.Equals(normalizedParameterType, normalizedRecordType, StringComparison.Ordinal);
    }

    private static string NormalizeComparisonType(string value)
    {
        return value
            .Replace("const ", string.Empty, StringComparison.Ordinal)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static bool IsMarshallerBackedWrapperSurface(RecordBinding record)
    {
        return record.SymbolId.StartsWith("import:", StringComparison.Ordinal) ||
            record.GetMetadata<bool>(MetadataKeys.MarshallerConfiguredRecord) ||
            !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) ||
            record.Members.OfType<FunctionBinding>().Any(static member => member.HasMetadata(MetadataKeys.MarshallerExposedMethod));
    }

    private static bool TryGetNamespaceExclusionReason(
        string cppQualifiedName,
        IReadOnlySet<string> allowedNamespaceRoots,
        out string reason)
    {
        if (allowedNamespaceRoots.Count == 0)
        {
            reason = string.Empty;
            return false;
        }

        var separatorIndex = cppQualifiedName.IndexOf("::", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            reason = $"global declaration '{cppQualifiedName}' is outside the configured namespace mappings for this compilation";
            return true;
        }

        var namespaceRoot = cppQualifiedName[..separatorIndex];
        if (allowedNamespaceRoots.Contains(namespaceRoot))
        {
            // Check for internal detail namespaces — a universal C++ convention
            // indicating implementation details that should not be wrapped.
            if (cppQualifiedName.Contains("::detail::", StringComparison.Ordinal) ||
                cppQualifiedName.Contains("::Detail::", StringComparison.Ordinal) ||
                cppQualifiedName.EndsWith("::detail", StringComparison.Ordinal) ||
                cppQualifiedName.EndsWith("::Detail", StringComparison.Ordinal))
            {
                reason = $"declaration is in an internal detail namespace: '{cppQualifiedName}'";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        reason = $"declaration namespace root '{namespaceRoot}' is outside the configured namespace mappings for this compilation";
        return true;
    }

    private static string GetCallableSignatureKey(FunctionBinding function)
    {
        return string.Join(
            "|",
            function.CppQualifiedName,
            function.IsStatic,
            function.IsConstructor,
            string.Join(";", function.Parameters.Select(static parameter => parameter.CppTypeName)));
    }

    private static string GetManagedCallableSignatureKey(FunctionBinding function)
    {
        return string.Join(
            "|",
            function.ManagedName,
            function.IsStatic,
            function.IsConstructor,
            GetGenericParameterSignature(function),
            string.Join(";", function.Parameters.Select(static parameter => parameter.CppTypeName)));
    }

    private static string GetManagedPublicCallableSignatureKey(FunctionBinding function)
    {
        return string.Join(
            "|",
            function.ManagedName,
            function.IsStatic,
            function.IsConstructor,
            GetGenericParameterSignature(function),
            string.Join(
                ";",
                function.Parameters.Select(static parameter =>
                    string.Join(
                        ":",
                        parameter.Direction,
                        parameter.AbiKind,
                        parameter.Type.Kind,
                        parameter.Type.UnaliasedManagedTypeName))));
    }

    private static string GetGenericParameterSignature(FunctionBinding function)
    {
        return string.Join(
            ",",
            function.GenericParameters.Select(static parameter => $"{parameter.Position}:{parameter.Name}"));
    }

    /// <summary>
    /// Returns <c>true</c> when the type itself or any element in its
    /// generic-argument tree is an open generic parameter (i.e.
    /// <see cref="BindingTypeKind.GenericParameter"/>).  This is more precise
    /// than <see cref="BindingType.ContainsGenericParameters"/> because it
    /// directly inspects the <see cref="BindingType.Kind"/> rather than
    /// relying on the cached property, which can produce false positives
    /// when generic arguments are inherited from a template record's default
    /// <c>HandleType</c>.
    /// </summary>
    private static bool ContainsOpenGenericParameter(BindingType type)
    {
        if (type.Kind == BindingTypeKind.GenericParameter)
        {
            return true;
        }

        foreach (var argument in type.GenericArguments)
        {
            if (ContainsOpenGenericParameter(argument))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a type (or any of its generic arguments) uses a ref struct
    /// as a generic type argument.  C# does not allow ref structs (e.g.
    /// <see cref="Span{T}"/>, <see cref="ReadOnlySpan{T}"/>) to be used as type
    /// parameters, so any <c>Container&lt;ReadOnlySpan&lt;byte&gt;&gt;</c>
    /// instantiation would produce invalid C#.
    /// </summary>
    private static bool HasRefStructGenericArgument(BindingType type)
    {
        foreach (var argument in type.GenericArguments)
        {
            // Span projections (ReadOnlySpan<byte> for UTF-8 views, Span<T> and
            // ReadOnlySpan<T> for projected-value arrays) are the ref structs the
            // marshaller system maps; see MarshallerDescriptor.IsRefStructTypeName.
            if (IsArgumentRefStruct(argument))
            {
                return true;
            }

            if (HasRefStructGenericArgument(argument))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsArgumentRefStruct(BindingType argument)
    {
        return argument.Marshaller.IsRefStruct;
    }

}
