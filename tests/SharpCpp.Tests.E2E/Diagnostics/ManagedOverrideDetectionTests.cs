using Xunit;

namespace SharpCpp.Tests.E2E.Diagnostics;

/// <summary>The generated bindings detect managed overrides by slot key, without reflection.</summary>
public sealed class ManagedOverrideDetectionTests
{
    private static IEnumerable<(string File, string[] Lines)> ReadGeneratedSources() =>
        Directory.EnumerateFiles(GeneratedArtifacts.Directory, "*.g.cs")
            .Select(static path => (Path.GetFileName(path), GeneratedArtifacts.ReadText(Path.GetFileName(path)).Split('\n')));

    [Fact]
    public void Generated_accessors_use_slot_keys_instead_of_reflection()
    {
        var accessorCount = 0;
        foreach (var (_, lines) in ReadGeneratedSources())
        {
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (!line.Contains("nint __Get", StringComparison.Ordinal) || !line.Contains("Pointer(", StringComparison.Ordinal))
                {
                    continue;
                }

                accessorCount++;
                var accessor = string.Join('\n', lines.Skip(index).Take(5));
                Assert.DoesNotContain("(Type ", line, StringComparison.Ordinal);
                Assert.DoesNotContain("Type[]", accessor, StringComparison.Ordinal);
                Assert.DoesNotContain("typeof(", accessor, StringComparison.Ordinal);
                Assert.DoesNotContain("GetMethod", accessor, StringComparison.Ordinal);
                Assert.Contains("self.IsManagedOverride(\"", accessor, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("HasManagedOverride", string.Join('\n', lines), StringComparison.Ordinal);
            Assert.DoesNotContain("GetMethod(", string.Join('\n', lines), StringComparison.Ordinal);
        }

        Assert.True(accessorCount > 0, "The E2E bindings contain no virtual callback accessor.");
    }

    [Fact]
    public void Generated_connect_director_does_not_inspect_the_runtime_type()
    {
        var connectCount = 0;
        foreach (var (_, lines) in ReadGeneratedSources())
        {
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains("private void ConnectDirector()", StringComparison.Ordinal))
                {
                    continue;
                }

                connectCount++;
                var body = string.Join('\n', lines.Skip(index).Take(4));
                Assert.DoesNotContain("GetType()", body, StringComparison.Ordinal);
                Assert.DoesNotContain("runtimeType", body, StringComparison.Ordinal);
            }
        }

        Assert.True(connectCount > 0, "The E2E bindings contain no ConnectDirector method.");
    }

    [Fact]
    public void Generated_virtual_methods_carry_their_slot_key()
    {
        var text = string.Join('\n', ReadGeneratedSources().SelectMany(static source => source.Lines));

        Assert.Contains("[global::SharpCpp.Support.ManagedVirtualSlot(\"SharpCpp.Tests.E2E.Generated.CallbackCounter.GetValue()\")]", text, StringComparison.Ordinal);
        Assert.Contains("self.IsManagedOverride(\"SharpCpp.Tests.E2E.Generated.CallbackCounter.GetValue()\")", text, StringComparison.Ordinal);
    }
}
