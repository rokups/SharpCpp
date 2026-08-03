using System.Text;

namespace SharpCpp.Model.Utilities;

/// <summary>Stable compact names shared by metadata producers, importers, and renderers.</summary>
public static class SharedWrapperNaming
{
    public static string GetTypeToken(string cppTypeName)
    {
        ArgumentNullException.ThrowIfNull(cppTypeName);

        var basename = cppTypeName;
        var genericStart = basename.IndexOf('<');
        if (genericStart >= 0)
        {
            basename = basename[..genericStart];
        }

        var namespaceSeparator = basename.LastIndexOf("::", StringComparison.Ordinal);
        if (namespaceSeparator >= 0)
        {
            basename = basename[(namespaceSeparator + 2)..];
        }

        basename = Sanitize(basename);
        return $"{(basename.Length == 0 ? "shared" : basename)}_{GetHashToken(cppTypeName)}";
    }

    public static string GetMemberToken(string memberName, string stableIdentity)
    {
        ArgumentNullException.ThrowIfNull(memberName);
        ArgumentNullException.ThrowIfNull(stableIdentity);

        var basename = Sanitize(memberName);
        return $"{(basename.Length == 0 ? "member" : basename)}_{GetHashToken(stableIdentity)}";
    }

    public static string GetHashToken(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        uint hash = 2166136261;
        foreach (var character in value)
        {
            hash = unchecked((hash ^ character) * 16777619);
        }

        return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        return builder.ToString().Trim('_');
    }
}
