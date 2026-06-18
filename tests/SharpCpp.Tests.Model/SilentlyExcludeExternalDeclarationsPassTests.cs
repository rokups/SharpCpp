using SharpCpp.Model.Ast;
using SharpCpp.Pipeline;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class SilentlyExcludeExternalDeclarationsPassTests
{
    [Fact]
    public void Apply_silently_excludes_unconfigured_placeholder_records()
    {
        var compilation = new BindingCompilation("TestProject", "Test", "Bindings", "Native", ["Sample.hpp"]);
        var placeholder = new RecordBinding("record:external::Widget", "Widget", "Widget", "external::Widget");
        placeholder.SetMetadata(MetadataKeys.PlaceholderRecord, true);
        compilation.Members.Add(placeholder);

        SilentlyExcludeExternalDeclarationsPass.Apply(compilation);

        Assert.True(placeholder.IsIgnored());
        Assert.True(placeholder.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_keeps_explicitly_configured_placeholder_records_visible()
    {
        var compilation = new BindingCompilation("TestProject", "Test", "Bindings", "Native", ["Sample.hpp"]);
        var placeholder = new RecordBinding("record:std::basic_string", "basic_string", "String", "std::basic_string")
        {
            ManagedBaseTypeName = "StringBase"
        };
        placeholder.SetMetadata(MetadataKeys.PlaceholderRecord, true);
        compilation.Members.Add(placeholder);

        SilentlyExcludeExternalDeclarationsPass.Apply(compilation);

        Assert.False(placeholder.IsIgnored());
        Assert.False(placeholder.IsSilentlyExcluded());
    }
}
