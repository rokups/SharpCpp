using Xunit;

namespace SharpCpp.Tests.E2E.Ignore;

public sealed class IgnoreTests
{
    [Fact]
    public void Ignored_record_produces_comment_stub_in_generated_output()
    {
        var content = ReadAllGeneratedCSharpContent();

        Assert.Contains("// class SkippedRecord; // excluded: marked [[sharpcpp::ignore]]", content, StringComparison.Ordinal);
        Assert.DoesNotContain("public partial class SkippedRecord", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignored_free_function_produces_comment_stub_in_generated_output()
    {
        var content = ReadAllGeneratedCSharpContent();

        Assert.Contains("// public static int SkippedFreeFunction(int x); // excluded: manual stub", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\n    public static int SkippedFreeFunction(", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignored_enum_produces_comment_stub_in_generated_output()
    {
        var content = ReadAllGeneratedCSharpContent();

        Assert.Contains("// enum SkippedEnum; // excluded: marked [[sharpcpp::ignore]]", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignored_record_with_reason_includes_reason_in_stub()
    {
        var content = ReadAllGeneratedCSharpContent();

        Assert.Contains("// class SkippedRecordWithReason; // excluded: not yet supported", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignored_members_within_record_produce_comment_stubs()
    {
        var content = ReadAllGeneratedCSharpContent();

        Assert.Contains("// public int InternalMethod(); // excluded: internal use only", content, StringComparison.Ordinal);
        Assert.Contains("// public int SkippedField; // excluded: marked [[sharpcpp::ignore]]", content, StringComparison.Ordinal);
        Assert.Contains("// public static int SkippedStatic(); // excluded: marked [[sharpcpp::ignore]]", content, StringComparison.Ordinal);
        Assert.Contains("// public PartiallyIgnoredRecord(int debug_value); // excluded: debug constructor", content, StringComparison.Ordinal);
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
