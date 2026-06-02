using SharpCpp.Model.Ast;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class BindingNodeTests
{
    [Fact]
    public void Adding_function_members_tracks_parent_relationships()
    {
        var compilation = new BindingCompilation("TestProject", "Test.Namespace", "Bindings", "Test.Native", new[] { "a.hpp" });
        var function = new FunctionBinding("function:add_numbers", "add_numbers", "AddNumbers", BindingType.Int32);
        var left = new ParameterBinding("parameter:add_numbers:lhs", "lhs", "lhs", BindingType.Int32);
        var right = new ParameterBinding("parameter:add_numbers:rhs", "rhs", "rhs", BindingType.Int32);

        function.Parameters.Add(left);
        function.Parameters.Add(right);
        compilation.Members.Add(function);

        Assert.Same(compilation, function.Parent);
        Assert.Same(function, left.Parent);
        Assert.Same(function, right.Parent);
        Assert.Equal(new[] { function }, compilation.Functions);
        Assert.Equal(2, compilation.Descendants<ParameterBinding>().Count());
    }

    [Fact]
    public void Metadata_round_trips_from_node_bag()
    {
        var record = new RecordBinding("record:Widget", "Widget", "Widget");

        record.SetMetadata(MetadataKeys.CanonicalCppIdentity, "namespace::Widget");

        Assert.True(record.HasMetadata(MetadataKeys.CanonicalCppIdentity));
        Assert.True(record.TryGetMetadata<string>(MetadataKeys.CanonicalCppIdentity, out var value));
        Assert.Equal("namespace::Widget", value);
    }

    [Fact]
    public void Detach_removes_child_from_parent_collection()
    {
        var function = new FunctionBinding("function:add_numbers", "add_numbers", "AddNumbers", BindingType.Int32);
        var left = new ParameterBinding("parameter:add_numbers:lhs", "lhs", "lhs", BindingType.Int32);

        function.Parameters.Add(left);
        left.Detach();

        Assert.Empty(function.Parameters);
        Assert.Null(left.Parent);
    }
}
