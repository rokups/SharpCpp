using SharpCpp.Model.Ast;
using SharpCpp.Parsing;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class ClangSharpHeaderParserFlattenedFieldTests
{
    [Fact]
    public void Parse_flattens_anonymous_record_fields_with_native_access_paths()
    {
        var compilation = ParseTempHeader(
            "Sample.h",
            """
            typedef struct SdlSysWMinfo
            {
                int version;
                union
                {
                    struct
                    {
                        void* display;
                        unsigned long window;
                    } x11;
                } info;
            } SdlSysWMinfo;
            """,
            ["-xc", "-std=c17"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "SdlSysWMinfo");
        var display = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "info_x11_display");
        var window = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "info_x11_window");

        Assert.Equal("InfoX11Display", display.ManagedName);
        Assert.Equal("info.x11.display", display.GetMetadata<string>(MetadataKeys.NativeFieldAccessExpression));
        Assert.Equal("void*", display.CppTypeName);
        Assert.Equal(BindingPrimitiveKind.IntPtr, display.Type.PrimitiveKind);
        Assert.Equal("InfoX11Window", window.ManagedName);
        Assert.Equal("info.x11.window", window.GetMetadata<string>(MetadataKeys.NativeFieldAccessExpression));
    }

    [Fact]
    public void Parse_composes_nested_flattened_access_paths_from_named_record_values()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            namespace Test
            {
            struct StencilDesc
            {
                int StencilFailOp;
            };

            struct DepthStencilDesc
            {
                StencilDesc BackFace;
            };

            struct GraphicsPipelineDesc
            {
                DepthStencilDesc DepthStencilDesc;
            };

            struct PipelineStateDesc
            {
                GraphicsPipelineDesc GraphicsPipeline;
            };
            }
            """,
            ["-x", "c++", "-std=c++20"]);

        var graphicsPipeline = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Test::GraphicsPipelineDesc");
        var depthStencil = Assert.Single(graphicsPipeline.Members.OfType<FieldBinding>(), static field => field.NativeName == "DepthStencilDesc_BackFace_StencilFailOp");
        Assert.Equal("DepthStencilDesc.BackFace.StencilFailOp", depthStencil.GetMetadata<string>(MetadataKeys.NativeFieldAccessExpression));

        var pipelineState = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Test::PipelineStateDesc");
        var field = Assert.Single(pipelineState.Members.OfType<FieldBinding>(), static field => field.NativeName == "GraphicsPipeline_DepthStencilDesc_BackFace_StencilFailOp");

        Assert.Equal("GraphicsPipelineDepthStencilDescBackFaceStencilFailOp", field.ManagedName);
        Assert.Equal("GraphicsPipeline.DepthStencilDesc.BackFace.StencilFailOp", field.GetMetadata<string>(MetadataKeys.NativeFieldAccessExpression));
    }

    private static BindingCompilation ParseTempHeader(string fileName, string source, string[] clangArgs)
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), "sharpcpp-flattened-fields-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDir);
        try
        {
            var headerPath = Path.Combine(fixtureDir, fileName);
            File.WriteAllText(headerPath, source);

            var parser = new ClangSharpHeaderParser();
            return parser.Parse(new ClangSharpParseRequest(
                ProjectName: "TestProject",
                ManagedNamespace: "Test.Namespace",
                ManagedClassName: "Bindings",
                NativeLibraryName: "Test.Native",
                HeaderFilePath: headerPath,
                HeaderIncludePath: fileName,
                ClangArguments: [.. clangArgs, $"-I{fixtureDir}"],
                MarshallerConfigurationFiles: Array.Empty<string>(),
                EntryPointPrefix: "Test_"));
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }
}
