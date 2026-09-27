using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class ConstructorDefaultBreadthTests
{
    [Fact]
    public void Constructor_default_branch_matrix_round_trips_native_state()
    {
        using var firstDefault = new ConstructorDefaultFirst();
        using var firstExplicit = new ConstructorDefaultFirst("four"u8);
        Assert.Equal(6, firstDefault.GetValue());
        Assert.Equal(4, firstExplicit.GetValue());

        using var macroDefault = new ConstructorDefaultMacroBefore(null);
        using var macroExplicit = new ConstructorDefaultMacroBefore(11, "four"u8);
        Assert.Equal(10, macroDefault.GetValue());
        Assert.Equal(15, macroExplicit.GetValue());

        var value = 39;
        using var refDefault = new ConstructorDefaultRefBefore(ref value);
        Assert.Equal(40, value);
        Assert.Equal(42, refDefault.GetValue());

        using var refExplicit = new ConstructorDefaultRefBefore(ref value, "z"u8);
        Assert.Equal(41, value);
        Assert.Equal(42, refExplicit.GetValue());
    }
}
