using Xunit;

namespace SharpCpp.Tests.E2E.Ignore;

public sealed class IgnoreTests
{
    [Fact]
    public void Ignored_record_produces_comment_stub_in_generated_output()
    {
        var content = ReadAllGeneratedCSharpContent();

        var lines = content.Replace("\r\n", "\n").Split('\n');

        Assert.Contains("    // struct skipped_record { int value; int get_value() const; } // excluded: marked [[sharpcpp::ignore]]", lines);
        Assert.DoesNotContain("public partial class SkippedRecord", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignored_free_function_produces_comment_stub_in_generated_output()
    {
        var content = ReadAllGeneratedCSharpContent();

        var lines = content.Replace("\r\n", "\n").Split('\n');

        Assert.Contains("        // inline int skipped_free_function(int x) // excluded: manual stub", lines);
        Assert.DoesNotContain("\n    public static int SkippedFreeFunction(", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignored_enum_produces_comment_stub_in_generated_output()
    {
        var content = ReadAllGeneratedCSharpContent();

        var lines = content.Replace("\r\n", "\n").Split('\n');

        Assert.Contains("    // enum class skipped_enum : int { value_a = 0, value_b = 1 } // excluded: marked [[sharpcpp::ignore]]", lines);
    }

    [Fact]
    public void Macro_backed_ignored_declarations_use_decl_spelling_not_macro_invocation()
    {
        var content = ReadAllGeneratedCSharpContent();

        var lines = content.Replace("\r\n", "\n").Split('\n');

        Assert.Contains(lines, static line => line.Contains("macro_generated_macro_record", StringComparison.Ordinal) && line.Contains("excluded: marked [[sharpcpp::ignore]]", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.Contains("macro_generated_macro_enum", StringComparison.Ordinal) && line.Contains("excluded: marked [[sharpcpp::ignore]]", StringComparison.Ordinal));
        Assert.DoesNotContain("SHARPCPP_IGNORED_PAIR", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignored_record_with_reason_includes_reason_in_stub()
    {
        var content = ReadAllGeneratedCSharpContent();

        var lines = content.Replace("\r\n", "\n").Split('\n');

        Assert.Contains("    // struct skipped_record_with_reason { int count; } // excluded: not yet supported", lines);
    }

    [Fact]
    public void Ignored_members_within_record_produce_comment_stubs()
    {
        var content = ReadAllGeneratedCSharpContent();

        var lines = content.Replace("\r\n", "\n").Split('\n');

        Assert.Contains("        // int internal_method() // excluded: internal use only", lines);
        Assert.Contains("        // int skipped_field // excluded: marked [[sharpcpp::ignore]]", lines);
        Assert.Contains("        // static int skipped_static() // excluded: marked [[sharpcpp::ignore]]", lines);
        Assert.Contains("        // partially_ignored_record(int debug_value) // excluded: debug constructor", lines);
        Assert.Contains("        // partially_ignored_record operator+(const partially_ignored_record &other) const // excluded: marked [[sharpcpp::ignore]]", lines);
    }

    private static string ReadAllGeneratedCSharpContent()
    {
        var generatedDir = GetGeneratedDir();
        var files = Directory.GetFiles(generatedDir, "*.g.cs", SearchOption.TopDirectoryOnly);
        return string.Join("\n", files.Select(File.ReadAllText));
    }

    private static string GetGeneratedDir()
    {
        var dir = Environment.GetEnvironmentVariable("SharpCppGeneratedDir");
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                "..", "..", "..", "..", "..",
                "out", "build", "dev", "generated", "sharpcpp-e2e");
        }

        return Path.GetFullPath(dir);
    }
}
