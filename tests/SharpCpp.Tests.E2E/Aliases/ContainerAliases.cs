using System.Runtime.InteropServices;

using Container_aliases;

using ContainerAliases;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Aliases;

[StructLayout(LayoutKind.Sequential)]
public record struct Vector3f(float X, float Y, float Z);

public sealed class ContainerAliasTests
{
    [Fact]
    public void Value_alias_parameters_use_the_configured_struct_and_native_constructor_defaults()
    {
        using var builder = new ActionBuilder();
        Assert.Equal(321f, builder.JumpBy(new Vector3f(1, 2, 3)));
        Assert.Equal(321f, builder.MoveBy());
        Assert.Equal(1f, builder.MoveBy(new Vector3f(1, 0, 0)));
    }

    [Fact]
    public void Vectors_of_value_aliases_keep_qualified_native_element_types()
    {
        using var hull = new HullBuilder();
        using var vertices = new Vector<Vector3f>();
        vertices.Add(new Vector3f(1, 2, 3));
        hull.SetVertices(vertices);
        Assert.Equal(new Vector3f(1, 2, 3), Assert.Single(hull.GetVertices()));
        Assert.Equal(new Vector3f(1, 2, 3), Assert.Single(hull.Stored));
    }

    [Fact]
    public void Vectors_of_record_handles_and_aliased_vectors_are_parameters()
    {
        using var hub = new EventHub();
        using var first = new Container_aliases.Object(2);
        using var second = new Container_aliases.Object(3);
        using var exceptions = new Vector<Container_aliases.Object>();
        exceptions.Add(first);
        exceptions.Add(second);
        Assert.Equal(5, hub.UnsubscribeExcept(exceptions));

        using var consumer = new IntVectorConsumer();
        using var items = new Vector<int>();
        items.Add(1);
        Assert.Equal(1, consumer.Accept(items));
    }

    [Fact]
    public void Map_alias_returns_index_by_projected_key()
    {
        using var hub = new DataHub();
        Assert.Equal(70, hub.GetEventDataMap()[7].GetValue());
    }

    [Fact]
    public void String_keyed_map_alias_returns_index_by_string()
    {
        using var hub = new DataHub();
        using var key = new Std.String("seven");
        Assert.Equal(77, hub.GetStringEventDataMap()[key].GetValue());
    }

    [Fact]
    public void Map_alias_callback_arguments_are_wrapped()
    {
        using var hub = new DataHub();
        using var receiver = new Container_aliases.Object(4);
        Assert.Equal(74, hub.Send(receiver, (target, hash, data) => target.GetId() + data[hash].GetValue()));
    }

    [Fact]
    public void Alias_template_allocators_keep_the_configured_string()
    {
        using var loader = new Loader();
        Assert.Equal("none", loader.GetError().ToString());
        using var alias = new Std.String();
        using var std = new Std.String();
        Assert.True(loader.Load(alias));
        Assert.True(loader.LoadStd(std));
        Assert.Equal("alias", alias.ToString());
        Assert.Equal("std", std.ToString());
    }

    [Fact]
    public void Inline_namespaces_and_same_named_records_bind_by_qualified_identity()
    {
        using var inlineValue = new InlineValue();
        Assert.Equal(3, inlineValue.Value());
        Assert.Equal("Container_aliases", typeof(InlineValue).Namespace);

        using var alpha = new Container_aliases.Alpha.Context();
        using var beta = new Container_aliases.Beta.Context();
        Assert.Equal(1, alpha.Touch());
        Assert.Equal(2, beta.Touch());
    }

    [Fact]
    public void All_caps_enumerators_take_managed_names()
    {
        Assert.Equal(new[] { "Outside", "Intersects", "Inside" }, System.Enum.GetNames<Intersection>());
        Assert.Equal(Intersection.Intersects, E2EBindings.Classify(0));
    }
}
