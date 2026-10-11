using System.Text;

namespace SharpCpp.ApiUnion;

/// <summary>Renders a readable report of a union: counts, partial members and detail conflicts.</summary>
public static class UnionReport
{
    public static string Render(ApiUnionResult union)
    {
        var builder = new StringBuilder();
        var all = union.Platforms.Count;

        builder.AppendLine($"Platforms: {string.Join(", ", union.Platforms)}");
        builder.AppendLine($"Union members: {union.Members.Count}");
        builder.AppendLine();

        builder.AppendLine("== Members by kind and platform set ==");
        var byPattern = union.Members
            .GroupBy(member => (member.Member.Kind, Pattern: string.Join(",", member.Platforms)))
            .OrderBy(group => group.Key.Kind)
            .ThenByDescending(group => group.Count());
        foreach (var group in byPattern)
        {
            builder.AppendLine($"{group.Key.Kind,-9} {group.Key.Pattern,-32} {group.Count()}");
        }

        builder.AppendLine();
        builder.AppendLine("== Types absent on some platform ==");
        foreach (var member in union.Members.Where(member => member.Member.Kind == ApiMemberKind.Type && member.Platforms.Count < all))
        {
            builder.AppendLine($"{member.Member.TypeName}  present_on={string.Join(",", member.Platforms)}");
        }

        builder.AppendLine();
        builder.AppendLine("== Members absent on some platform (type present everywhere) ==");
        var completeTypes = union.Members
            .Where(member => member.Member.Kind == ApiMemberKind.Type && member.Platforms.Count == all)
            .Select(member => member.Member.TypeName)
            .ToHashSet(StringComparer.Ordinal);
        var partialMembers = union.Members
            .Where(member => member.Member.Kind != ApiMemberKind.Type && member.Platforms.Count < all && completeTypes.Contains(member.Member.TypeName))
            .ToList();
        foreach (var group in partialMembers.GroupBy(member => member.Member.TypeName).OrderByDescending(group => group.Count()))
        {
            builder.AppendLine($"{group.Key}: {group.Count()}");
            foreach (var member in group.Take(5))
            {
                builder.AppendLine($"    {member.Member.Key}  present_on={string.Join(",", member.Platforms)}");
            }

            if (group.Count() > 5)
            {
                builder.AppendLine($"    ... {group.Count() - 5} more");
            }
        }

        builder.AppendLine();
        builder.AppendLine("== Detail conflicts (same member, different facts) ==");
        var conflicts = union.Members.Where(member => member.HasDetailConflict).ToList();
        builder.AppendLine($"Count: {conflicts.Count}");
        foreach (var member in conflicts.Take(200))
        {
            builder.AppendLine(member.Member.Key);
            foreach (var (platform, details) in member.DetailsByPlatform.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                builder.AppendLine($"    {platform}: {details}");
            }
        }

        if (conflicts.Count > 200)
        {
            builder.AppendLine($"... {conflicts.Count - 200} more");
        }

        return builder.ToString();
    }
}
