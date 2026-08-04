using SharpCpp.Tests.E2E.Generated;

using Xunit;

using RenamedDayOfWeek = SharpCpp.Tests.E2E.Generated.DayOfWeek;

namespace SharpCpp.Tests.E2E.Rename;

public sealed class RenameTests
{
    [Fact]
    public void Renamed_record_calls_native_members()
    {
        using var instance = new RenamedStruct();

        Assert.Equal(-1, instance.GetValue());
        instance.Value = 17;
        Assert.Equal(17, instance.GetValue());
    }

    [Fact]
    public void Generic_record_renamed_with_format_placeholder()
    {
        using var holder = E2EBindings.MakeIntHolder(42);
        Assert.Equal(42, holder.GetItem());
    }

    [Fact]
    public void Renamed_enum_roundtrips_through_native_binding()
    {
        Assert.Equal(RenamedDayOfWeek.Tuesday, E2EBindings.RoundtripWeekday(RenamedDayOfWeek.Tuesday));
    }

    [Fact]
    public void Free_function_renamed_emits_with_new_name()
    {
        Assert.Equal(7, E2EBindings.AddValues(3, 4));
    }

    [Fact]
    public void Instance_method_renamed_emits_with_new_name()
    {
        using var instance = new ApiObject();

        instance.IncrementBy(5);
        Assert.Equal(5, instance.GetCount());
    }

    [Fact]
    public void Static_method_renamed_emits_with_new_name()
    {
        using var instance = ApiObject.MakeDefault();

        instance.IncrementBy(4);
        Assert.Equal(4, instance.GetCount());
    }

    [Fact]
    public void Field_renamed_emits_with_new_name()
    {
        using var instance = new ApiObject();

        instance.ItemCount = 9;
        Assert.Equal(9, instance.ItemCount);
        Assert.Equal(9, instance.GetCount());
    }

    [Fact]
    public void Record_renamed_with_name_placeholder_uses_pascal_case_substitution()
    {
        using var store = new DataStoreWrapper();

        store.Items = 6;
        Assert.Equal(6, store.Items);
    }
}
