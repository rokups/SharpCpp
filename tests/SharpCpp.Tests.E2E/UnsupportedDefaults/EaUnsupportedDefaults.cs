using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.UnsupportedDefaults;

// The ea::function forms of the callback default tests.
public sealed class EaUnsupportedDefaultsTests
{
    [Fact]
    public void Ea_Record_pointer_default_before_callback_default_accepts_null_and_a_value()
    {
        using var item = new UnsupportedDefaultItem { Value = 5 };

        Assert.Equal(9, E2EBindings.UnsupportedDefaultRecordPointerThenEaCallback(10));
        Assert.Equal(9, E2EBindings.UnsupportedDefaultRecordPointerThenEaCallback(10, null));
        Assert.Equal(15, E2EBindings.UnsupportedDefaultRecordPointerThenEaCallback(10, item));
        Assert.Equal(90, E2EBindings.UnsupportedDefaultRecordPointerThenEaCallback(10, null, value => value * 10));
        Assert.Equal(150, E2EBindings.UnsupportedDefaultRecordPointerThenEaCallback(10, item, value => value * 10));
    }

    [Fact]
    public void Ea_Vector_pointer_default_before_callback_default_accepts_null_and_a_value()
    {
        using var values = new global::Std.Vector<int>((IEnumerable<int>)new[] { 1, 2, 3 });

        Assert.Equal(9, E2EBindings.UnsupportedDefaultVectorPointerThenEaCallback(10));
        Assert.Equal(9, E2EBindings.UnsupportedDefaultVectorPointerThenEaCallback(10, null));
        Assert.Equal(13, E2EBindings.UnsupportedDefaultVectorPointerThenEaCallback(10, values));
        Assert.Equal(90, E2EBindings.UnsupportedDefaultVectorPointerThenEaCallback(10, null, value => value * 10));
        Assert.Equal(130, E2EBindings.UnsupportedDefaultVectorPointerThenEaCallback(10, values, value => value * 10));
    }

    [Fact]
    public void Ea_Consecutive_null_default_record_pointers_before_callback_default_accept_every_combination()
    {
        using var a = new UnsupportedDefaultItem { Value = 2 };
        using var b = new UnsupportedDefaultItem { Value = 3 };

        Assert.Equal(1, E2EBindings.UnsupportedDefaultTwoRecordPointersThenEaCallback(1));
        Assert.Equal(1, E2EBindings.UnsupportedDefaultTwoRecordPointersThenEaCallback(1, null, null));
        Assert.Equal(301, E2EBindings.UnsupportedDefaultTwoRecordPointersThenEaCallback(1, null, b));
        Assert.Equal(21, E2EBindings.UnsupportedDefaultTwoRecordPointersThenEaCallback(1, a, null));
        Assert.Equal(321, E2EBindings.UnsupportedDefaultTwoRecordPointersThenEaCallback(1, a, b));

        Assert.Equal(2, E2EBindings.UnsupportedDefaultTwoRecordPointersThenEaCallback(1, null, null, value => value * 2));
        Assert.Equal(602, E2EBindings.UnsupportedDefaultTwoRecordPointersThenEaCallback(1, null, b, value => value * 2));
        Assert.Equal(42, E2EBindings.UnsupportedDefaultTwoRecordPointersThenEaCallback(1, a, null, value => value * 2));
        Assert.Equal(642, E2EBindings.UnsupportedDefaultTwoRecordPointersThenEaCallback(1, a, b, value => value * 2));
    }

    [Fact]
    public void Ea_Null_default_callback_before_null_default_pointer_accepts_every_combination()
    {
        using var item = new UnsupportedDefaultItem { Value = 2 };

        Assert.Equal(5, E2EBindings.UnsupportedDefaultEaCallbackThenPointer(1));
        Assert.Equal(5, E2EBindings.UnsupportedDefaultEaCallbackThenPointer(1, null, null));
        Assert.Equal(25, E2EBindings.UnsupportedDefaultEaCallbackThenPointer(1, null, item));
        Assert.Equal(10, E2EBindings.UnsupportedDefaultEaCallbackThenPointer(1, value => value * 2, null));
        Assert.Equal(50, E2EBindings.UnsupportedDefaultEaCallbackThenPointer(1, value => value * 2, item));
        Assert.Equal(54, E2EBindings.UnsupportedDefaultEaCallbackThenPointer(1, value => value * 2, item, 6));
    }
}
