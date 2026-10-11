namespace SharpCpp.ApiUnion;

/// <summary>The kind of a public API member.</summary>
public enum ApiMemberKind
{
    Type,
    Field,
    Method,
    Property,
    Event
}

/// <summary>
/// One public type or member of an assembly, in a form that is equal across platforms when the
/// managed signature is equal. <see cref="Key"/> is the identity; <see cref="Details"/> holds
/// facts that may differ (a constant value, an enum underlying type) without a change of identity.
/// </summary>
public sealed record ApiMember(ApiMemberKind Kind, string TypeName, string Key, string Details)
{
    public override string ToString() => string.IsNullOrEmpty(Details) ? Key : $"{Key} [{Details}]";
}

/// <summary>The public API of one assembly, as read from its metadata.</summary>
public sealed class ApiSurface
{
    public ApiSurface(string platform, string assemblyName, IReadOnlyList<ApiMember> members)
    {
        Platform = platform;
        AssemblyName = assemblyName;
        Members = members;
        MembersByKey = members.ToDictionary(member => member.Key, StringComparer.Ordinal);
    }

    public string Platform { get; }

    public string AssemblyName { get; }

    public IReadOnlyList<ApiMember> Members { get; }

    public IReadOnlyDictionary<string, ApiMember> MembersByKey { get; }
}

/// <summary>One member of the union: the member and the platforms that have it.</summary>
public sealed record UnionMember(ApiMember Member, IReadOnlyList<string> Platforms, IReadOnlyDictionary<string, string> DetailsByPlatform)
{
    /// <summary>True when the member details are not the same on every platform that has it.</summary>
    public bool HasDetailConflict => DetailsByPlatform.Values.Distinct(StringComparer.Ordinal).Count() > 1;
}

/// <summary>The union of several platform surfaces.</summary>
public sealed class ApiUnionResult
{
    public ApiUnionResult(IReadOnlyList<string> platforms, IReadOnlyList<UnionMember> members)
    {
        Platforms = platforms;
        Members = members;
    }

    public IReadOnlyList<string> Platforms { get; }

    public IReadOnlyList<UnionMember> Members { get; }

    public static ApiUnionResult Build(IReadOnlyList<ApiSurface> surfaces)
    {
        var platforms = surfaces.Select(surface => surface.Platform).ToList();
        var byKey = new Dictionary<string, (ApiMember Member, List<string> Platforms, Dictionary<string, string> Details)>(StringComparer.Ordinal);

        foreach (var surface in surfaces)
        {
            foreach (var member in surface.Members)
            {
                if (!byKey.TryGetValue(member.Key, out var entry))
                {
                    entry = (member, [], new Dictionary<string, string>(StringComparer.Ordinal));
                    byKey.Add(member.Key, entry);
                }

                entry.Platforms.Add(surface.Platform);
                entry.Details[surface.Platform] = member.Details;
            }
        }

        var members = byKey.Values
            .Select(entry => new UnionMember(entry.Member, entry.Platforms, entry.Details))
            .OrderBy(member => member.Member.TypeName, StringComparer.Ordinal)
            .ThenBy(member => member.Member.Kind)
            .ThenBy(member => member.Member.Key, StringComparer.Ordinal)
            .ToList();

        return new ApiUnionResult(platforms, members);
    }
}
