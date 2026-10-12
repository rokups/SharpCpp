using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using SharpCpp.ApiUnion;
using Xunit;

namespace SharpCpp.Tests.ApiUnion;

public sealed class UnionTests
{
    private static readonly string[] FrameworkReferences = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
    private const string First = """
        namespace Fixture;
        public partial class Api {
            public static int Common() => 42;
            public static int Count() => 7;
            public static int Echo(int value) => value;
            public int Value { get => 9; }
        }
        public partial interface IFeature { static abstract int Kind(); int Common(); }
        public partial class Feature : IFeature { public static int Kind() => 1; public int Common() => 5; }
        public struct Value { public int X; }
        public enum Choice { First = 1 }
        public sealed class Singleton { private Singleton() {} public static Singleton Shared { get; } = new(); }
        """;
    private const string Second = """
        namespace Fixture;
        public partial class Api {
            public static int Common() => 43;
            public static nuint Count() => 8;
            public static long Echo(long value) => value;
            public static int OnlySecond() => 11;
            public int Value { get => 10; set {} }
            public class Nested<T> where T : unmanaged { public static T Echo(T value) => value; }
        }
        public partial interface IFeature { static abstract int Kind(); int Common(); int Extra(); }
        public partial class Feature : IFeature { public static int Kind() => 2; public int Common() => 6; public int Extra() => 8; }
        public struct Value { public int X; }
        public enum Choice { First = 1, Second = 2 }
        public sealed class Singleton { private Singleton() {} public static Singleton Shared { get; } = new(); }
        """;

    [Fact]
    public void Compiled_union_preserves_working_calls_and_throws_for_absent_APIs()
    {
        using var fixture = new FixtureBuild(First, Second);
        var reference = fixture.CompileReference();
        var consumerSource = """
            public static class Consumer {
                public static int Run() => Fixture.Api.Common();
                public static int Absent() => Fixture.Api.OnlySecond();
                public static long AbsentOverload() => Fixture.Api.Echo(12L);
                public static int Guarded() => System.OperatingSystem.IsLinux() ? Fixture.Api.Common() : Fixture.Api.OnlySecond();
                public static int InterfaceCall() => ((Fixture.IFeature)new Fixture.Feature()).Extra();
                public static int StaticInterfaceMethod() => Fixture.Feature.Kind();
                public static void Setter() => new Fixture.Api().Value = 1;
            }
            """;
        var consumer = fixture.Compile("Consumer", [Parse(consumerSource)], FrameworkReferences.Append(reference));
        var implementation = fixture.CompileImplementation("linux-x64", First);
        var context = new AssemblyLoadContext("UnionTest", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(implementation);
            var api = assembly.GetType("Fixture.Api")!;
            Assert.Equal(7, api.GetMethod("CountAsInt32")!.Invoke(null, null));
            Assert.Equal("Second", Enum.GetName(assembly.GetType("Fixture.Choice")!, 2));
            Assert.Throws<PlatformNotSupportedException>(() => Invoke(api, "CountAsUIntPtr"));
            var consumerType = context.LoadFromAssemblyPath(consumer).GetType("Consumer")!;
            Assert.Equal(42, Invoke(consumerType, "Run"));
            Assert.Equal(1, Invoke(consumerType, "StaticInterfaceMethod"));
            if (OperatingSystem.IsLinux()) Assert.Equal(42, Invoke(consumerType, "Guarded"));
            foreach (var name in new[] { "Absent", "AbsentOverload", "InterfaceCall", "Setter" })
                Assert.Throws<PlatformNotSupportedException>(() => Invoke(consumerType, name));
            var nested = assembly.GetType("Fixture.Api+Nested`1")!.MakeGenericType(typeof(int));
            Assert.Throws<PlatformNotSupportedException>(() => Invoke(nested, "Echo", 17));
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void Both_implementations_cover_the_reference_contract()
    {
        using var fixture = new FixtureBuild(First, Second);
        var reference = ApiSurfaceReader.Read("ref", fixture.CompileReference());
        foreach (var (platform, source) in new[] { ("linux-x64", First), ("win-x64", Second) })
        {
            var implementation = ApiSurfaceReader.Read(platform, fixture.CompileImplementation(platform, source));
            foreach (var member in reference.Members)
                Assert.True(implementation.MembersByKey.ContainsKey(member.Key), $"{platform}: missing {member.Key}");
        }
    }

    [Fact]
    public void Reference_preserves_extension_methods_and_interpolated_string_handlers()
    {
        const string source = """
            using System.Runtime.CompilerServices;
            namespace Fixture;
            [InterpolatedStringHandler]
            public ref struct Handler {
                public Handler(int literalLength, int formattedCount) {}
                public void AppendLiteral(string value) {}
                public void AppendFormatted<T>(T value) {}
            }
            public static class Log { public static int Format(this string text, ref Handler handler) => 5; }
            """;
        using var fixture = new FixtureBuild(source, source);
        var reference = fixture.CompileReference();
        fixture.Compile("Consumer", [Parse("using Fixture; public static class Consumer { public static int Run() => string.Empty.Format($\"test {7}\"); }")], FrameworkReferences.Append(reference));
    }

    [Fact]
    public void Platform_dependent_constants_and_enum_widths_fail_before_emitting()
    {
        using var fixture = new FixtureBuild();
        var first = fixture.Compile("Fixture", [Parse("namespace Fixture; public static class Api { public const int Value = 1; }")], FrameworkReferences, "first");
        var second = fixture.Compile("Fixture", [Parse("namespace Fixture; public static class Api { public const int Value = 2; }")], FrameworkReferences, "second");
        Assert.Contains("constant", Assert.Throws<InvalidOperationException>(() => new SourceEmitter(
            new Dictionary<string, string> { ["linux-x64"] = first, ["win-x64"] = second }, FrameworkReferences)).Message);
        first = fixture.Compile("Enums", [Parse("namespace Fixture; public enum Flag : int { A = 1 }")], FrameworkReferences, "first");
        second = fixture.Compile("Enums", [Parse("namespace Fixture; public enum Flag : uint { A = 1 }")], FrameworkReferences, "second");
        Assert.Contains("shape", Assert.Throws<InvalidOperationException>(() => new SourceEmitter(
            new Dictionary<string, string> { ["linux-x64"] = first, ["win-x64"] = second }, FrameworkReferences)).Message);
    }

    private static object? Invoke(Type type, string name, params object[] arguments)
    {
        try { return type.GetMethod(name)!.Invoke(null, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
    private static SyntaxTree Parse(string source) => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

    private sealed class FixtureBuild : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "SharpCpp.ApiUnion-" + Guid.NewGuid().ToString("N"));
        public FixtureBuild(string? first = null, string? second = null)
        {
            Directory.CreateDirectory(root);
            if (first is null || second is null) return;
            var inputs = new Dictionary<string, string>
            {
                ["linux-x64"] = Compile("Fixture", [Parse(first)], FrameworkReferences, "original-linux"),
                ["win-x64"] = Compile("Fixture", [Parse(second)], FrameworkReferences, "original-win")
            };
            new SourceEmitter(inputs, FrameworkReferences).Emit(Path.Combine(root, "sources"));
        }
        public string CompileReference() => Compile("Fixture", [Parse(File.ReadAllText(Path.Combine(root, "sources", "Reference.g.cs")))], FrameworkReferences, "ref", metadataOnly: true);
        public string CompileImplementation(string platform, string source)
        {
            var stub = Parse(File.ReadAllText(Path.Combine(root, "sources", platform, "Stubs.g.cs")));
            var trees = SourceAugmenter.Augment([Parse(source)], stub,
                File.ReadAllLines(Path.Combine(root, "sources", platform, "ConcreteTypes.txt")).ToHashSet());
            return Compile("Fixture", trees, FrameworkReferences, platform);
        }
        public string Compile(string name, IEnumerable<SyntaxTree> trees, IEnumerable<string> references,
            string? directory = null, bool metadataOnly = false)
        {
            var path = Path.Combine(root, directory ?? name, name + ".dll");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var compilation = CSharpCompilation.Create(name, trees,
                references.Select(reference => MetadataReference.CreateFromFile(reference)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                    nullableContextOptions: NullableContextOptions.Enable));
            using var stream = File.Create(path);
            var result = compilation.Emit(stream, options: new EmitOptions(metadataOnly: metadataOnly));
            Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
            return path;
        }
        public void Dispose()
        {
            // Windows keeps loaded assemblies locked until the collectible context is collected.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                try
                {
                    Directory.Delete(root, recursive: true);
                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
