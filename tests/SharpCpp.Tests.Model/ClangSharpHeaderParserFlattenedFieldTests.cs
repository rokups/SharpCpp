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

    [Fact]
    public void Parse_preserves_callable_records_nested_in_plain_data_fields()
    {
        var compilation = ParseTempHeader(
            "CallableNestedField.hpp",
            """
            struct curve
            {
                void clear();
                void add_point(float x, float y);
                int implementation_detail;
            };

            struct engine
            {
                curve torque;
                float maximum;
            };

            struct settings
            {
                engine engine_settings;
                curve friction;
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var settings = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "settings");
        var torque = Assert.Single(settings.Members.OfType<FieldBinding>(), static field => field.NativeName == "engine_settings_torque");
        var friction = Assert.Single(settings.Members.OfType<FieldBinding>(), static field => field.NativeName == "friction");

        Assert.Equal(BindingTypeKind.Handle, torque.Type.Kind);
        Assert.Equal("engine_settings.torque", torque.GetMetadata<string>(MetadataKeys.NativeFieldAccessExpression));
        Assert.Equal(BindingTypeKind.Handle, friction.Type.Kind);
        Assert.DoesNotContain(settings.Members.OfType<FieldBinding>(), static field => field.NativeName.Contains("implementation_detail", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_flattens_fields_of_callable_class_template_specializations()
    {
        var compilation = ParseTempHeader(
            "TemplateField.hpp",
            """
            template <class T> struct buffer
            {
                int size;
                T* data;
                void clear();
            };

            struct draw_list
            {
                buffer<int> vertices;
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var drawList = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "draw_list");
        var size = Assert.Single(drawList.Members.OfType<FieldBinding>(), static field => field.NativeName == "vertices_size");

        Assert.Equal("VerticesSize", size.ManagedName);
        Assert.Equal("vertices.size", size.GetMetadata<string>(MetadataKeys.NativeFieldAccessExpression));
    }

    [Fact]
    public void Parse_flattens_plain_data_records_with_only_static_helpers()
    {
        var compilation = ParseTempHeader(
            "StaticHelperNestedField.hpp",
            """
            struct features
            {
                int enabled;
                static bool equal(const features&, const features&);
                template <class T> static void enumerate(T&&);
                bool operator==(const features&) const;
            };

            struct settings
            {
                features requested;
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var settings = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "settings");
        var enabled = Assert.Single(settings.Members.OfType<FieldBinding>(), static field => field.NativeName == "requested_enabled");
        Assert.Equal("requested.enabled", enabled.GetMetadata<string>(MetadataKeys.NativeFieldAccessExpression));
    }

    [Fact]
    public void Parse_does_not_flatten_static_members_into_instance_paths()
    {
        var compilation = ParseTempHeader(
            "StaticNestedField.hpp",
            """
            struct nested
            {
                int value;
                inline static int shared_value = 1;
            };

            struct owner
            {
                nested state;
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "owner");

        Assert.Contains(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "state_value");
        Assert.DoesNotContain(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "state_shared_value");
    }

    [Fact]
    public void Parse_does_not_flatten_named_record_alias_storage()
    {
        var compilation = ParseTempHeader(
            "AliasedStorage.hpp",
            """
            struct vector
            {
                using storage = struct { float data[4]; };

                union
                {
                    storage value;
                    float components[4];
                };
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "vector");
        Assert.DoesNotContain(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "value_data");
        Assert.Contains(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "components");
    }

    [Fact]
    public void Parse_flattens_c_style_typedef_of_named_record()
    {
        var compilation = ParseTempHeader(
            "NamedTypedef.hpp",
            """
            struct features
            {
                int enabled;
            };
            typedef struct features features;

            struct settings
            {
                features capabilities;
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "settings");
        Assert.Contains(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "capabilities_enabled");
    }

    [Fact]
    public void Parse_flattens_c_style_typedef_of_anonymous_record()
    {
        var compilation = ParseTempHeader(
            "AnonymousTypedef.h",
            """
            typedef struct
            {
                int enabled;
            } features;

            typedef struct
            {
                features capabilities;
            } settings;
            """,
            ["-x", "c", "-std=c17"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "settings");
        Assert.Contains(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "capabilities_enabled");
    }

    [Fact]
    public void Parse_preserves_c_style_typedef_of_anonymous_union()
    {
        var compilation = ParseTempHeader(
            "AnonymousUnionTypedef.h",
            """
            typedef union
            {
                int integer;
                float scalar;
            } value;

            typedef struct
            {
                value data;
            } settings;
            """,
            ["-x", "c", "-std=c17"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "settings");
        Assert.Contains(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "data");
        Assert.DoesNotContain(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "data_integer");
    }

    [Fact]
    public void Parse_preserves_named_marshaller_backed_record_field()
    {
        var compilation = ParseTempHeader(
            "MarshallerBackedField.hpp",
            """
            struct vector3
            {
                float x;
                float y;
                float z;
            };

            struct body_settings
            {
                vector3 position;
            };

            namespace SharpCpp
            {
            template <class T> struct Marshaller;
            template <>
            struct [[sharpcpp::managed_type(public_type = "Vector3")]] Marshaller<vector3>
            {
            };
            }
            """,
            ["-x", "c++", "-std=c++20"],
            useHeaderAsMarshallerConfiguration: true);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "body_settings");
        var field = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "position");

        Assert.Equal("Position", field.ManagedName);
        Assert.Equal("Vector3", field.Type.ManagedTypeName);
        Assert.DoesNotContain(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "position_x");
    }

    [Fact]
    public void Parse_preserves_marshaller_backed_record_inside_flattened_data()
    {
        var compilation = ParseTempHeader(
            "NestedMarshallerBackedField.hpp",
            """
            struct vector3
            {
                float x;
                float y;
                float z;
            };

            struct material_data
            {
                vector3 color;
            };

            struct material
            {
                material_data data;
            };

            namespace SharpCpp
            {
            template <class T> struct Marshaller;
            template <>
            struct [[sharpcpp::managed_type(public_type = "Vector3")]] Marshaller<vector3>
            {
            };
            }
            """,
            ["-x", "c++", "-std=c++20"],
            useHeaderAsMarshallerConfiguration: true);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "material");
        var field = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "data_color");

        Assert.Equal("DataColor", field.ManagedName);
        Assert.Equal("Vector3", field.Type.ManagedTypeName);
        Assert.Equal("data.color", field.GetMetadata<string>(MetadataKeys.NativeFieldAccessExpression));
    }

    [Fact]
    public void Parse_does_not_apply_value_marshaller_to_flattened_pointer_field()
    {
        var compilation = ParseTempHeader(
            "NestedProjectedPointerField.hpp",
            """
            struct text_view
            {
                const char* begin;
                const char* end;
            };

            struct native_string
            {
                int size;
            };

            struct buffer
            {
                int size;
                text_view* data;
                native_string* name;
            };

            struct owner
            {
                buffer values;
            };

            namespace SharpCpp
            {
            template <class T> struct Marshaller;
            template <>
            struct [[sharpcpp::managed_type(public_type = "global::System.ReadOnlySpan<byte>")]] Marshaller<text_view>
            {
            };
            template <>
            struct [[sharpcpp::managed_type(
                public_type = "global::Std.String",
                managed_wrapper_assembly = "SharpCpp.Std"
            )]] Marshaller<native_string>
            {
            };
            }
            """,
            ["-x", "c++", "-std=c++20"],
            useHeaderAsMarshallerConfiguration: true);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "owner");
        var data = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "values_data");
        var name = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "values_name");

        Assert.Equal(BindingPrimitiveKind.IntPtr, data.Type.PrimitiveKind);
        Assert.Equal("global::System.IntPtr", data.Type.ManagedTypeName);
        Assert.Equal(BindingTypeKind.Handle, name.Type.Kind);
    }

    [Fact]
    public void Parse_preserves_protected_access_from_flattened_root_field()
    {
        var compilation = ParseTempHeader(
            "ProtectedFlattened.hpp",
            """
            class [[sharpcpp::director]] protected_container
            {
            public:
                virtual int get_value() const { return data.value; }

            protected:
                struct nested_value
                {
                    int value;
                } data;
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "protected_container");
        var field = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "data_value");

        Assert.Equal(BindingAccessModifier.Protected, field.AccessModifier);
        Assert.Equal("data.value", field.GetMetadata<string>(MetadataKeys.NativeFieldAccessExpression));
    }

    [Fact]
    public void Parse_does_not_synthesize_public_constructor_over_explicit_protected_constructor()
    {
        var compilation = ParseTempHeader(
            "ProtectedConstructor.hpp",
            """
            struct protected_constructor
            {
                int value;

            protected:
                protected_constructor() = default;
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "protected_constructor");

        Assert.DoesNotContain(record.Members.OfType<FunctionBinding>(), static function => function.IsConstructor);
    }

    [Fact]
    public void Parse_preserves_token_boundaries_in_lambda_native_defaults()
    {
        var compilation = ParseTempHeader(
            "LambdaDefault.hpp",
            """
            struct Time
            {
                static unsigned GetSystemTime();
            };

            using callback = unsigned (*)();
            void use_callback(callback value = [] { return Time::GetSystemTime(); });
            """,
            ["-x", "c++", "-std=c++20"]);

        var function = Assert.Single(compilation.Members.OfType<FunctionBinding>(), static function => function.NativeName == "use_callback");
        var parameter = Assert.Single(function.Parameters);
        var nativeDefault = parameter.GetMetadata<string>(MetadataKeys.NativeDefaultValueExpression);

        Assert.Contains("return Time :: GetSystemTime", nativeDefault, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_normalizes_spaced_enum_default_expressions_to_managed_names()
    {
        var compilation = ParseTempHeader(
            "EnumDefault.hpp",
            """
            enum class default_mode
            {
                active
            };

            void use_mode(default_mode mode = default_mode::active);
            """,
            ["-x", "c++", "-std=c++20"]);

        var function = Assert.Single(compilation.Members.OfType<FunctionBinding>(), static function => function.NativeName == "use_mode");
        var parameter = Assert.Single(function.Parameters);

        Assert.Equal("DefaultMode.Active", parameter.ManagedDefaultValueExpression);
    }

    [Fact]
    public void Parse_evaluates_managed_floating_point_defaults()
    {
        var compilation = ParseTempHeader(
            "FloatingDefaults.hpp",
            """
            void sample(float delay = -1.f, float alpha = .5f, double ratio = 1.25);
            """,
            ["-x", "c++", "-std=c++20"]);

        var function = Assert.Single(compilation.Members.OfType<FunctionBinding>());

        Assert.Equal("-1f", function.Parameters[0].ManagedDefaultValueExpression);
        Assert.Equal("0.5f", function.Parameters[1].ManagedDefaultValueExpression);
        Assert.Equal("1.25", function.Parameters[2].ManagedDefaultValueExpression);
    }

    private static BindingCompilation ParseTempHeader(
        string fileName, string source, string[] clangArgs, bool useHeaderAsMarshallerConfiguration = false)
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
                MarshallerConfigurationFiles: useHeaderAsMarshallerConfiguration ? [headerPath] : Array.Empty<string>(),
                EntryPointPrefix: "Test_"));
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }
}
