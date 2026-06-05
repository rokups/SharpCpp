using System.Reflection;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.UnsupportedDefaults;

public sealed class UnsupportedDefaultsTests
{
    [Fact]
    public void Single_unexpressible_default_is_emitted_as_nullable()
    {
        var method = typeof(E2EBindings).GetMethod(
            nameof(E2EBindings.CheckThresholdSingleDefault),
            BindingFlags.Public | BindingFlags.Static)!;

        var thresholdParam = method.GetParameters()[1];

        Assert.Equal("threshold", thresholdParam.Name);
        Assert.Equal(typeof(float?), thresholdParam.ParameterType);
        Assert.True(thresholdParam.HasDefaultValue);
        Assert.Null(thresholdParam.DefaultValue);
    }

    [Fact]
    public void Single_unexpressible_default_uses_cpp_default_when_omitted()
    {
        var result = E2EBindings.CheckThresholdSingleDefault(10.0f);

        Assert.Equal(0, result);
    }

    [Fact]
    public void Single_unexpressible_default_uses_explicit_value_when_provided()
    {
        var result = E2EBindings.CheckThresholdSingleDefault(100.0f, threshold: 50.0f);

        Assert.Equal(1, result);
    }

    [Fact]
    public void Two_unexpressible_defaults_both_emitted_as_nullable()
    {
        var method = typeof(E2EBindings).GetMethod(
            nameof(E2EBindings.CheckTwoDefaultsCascade),
            BindingFlags.Public | BindingFlags.Static)!;

        var aParam = method.GetParameters()[1];
        var bParam = method.GetParameters()[2];

        Assert.Equal(typeof(float?), aParam.ParameterType);
        Assert.Equal(typeof(float?), bParam.ParameterType);
        Assert.Null(aParam.DefaultValue);
        Assert.Null(bParam.DefaultValue);
    }

    [Fact]
    public void Two_unexpressible_defaults_both_omitted_uses_cpp_defaults()
    {
        var result = E2EBindings.CheckTwoDefaultsCascade(0.0f);

        Assert.Equal(0, result);
    }

    [Fact]
    public void Two_unexpressible_defaults_only_rightmost_omitted_cascades()
    {
        var result = E2EBindings.CheckTwoDefaultsCascade(0.0f, a: 25.0f);

        Assert.Equal(1, result);
    }

    [Fact]
    public void Two_unexpressible_defaults_both_provided_uses_explicit_values()
    {
        var result = E2EBindings.CheckTwoDefaultsCascade(0.0f, a: 30.0f, b: 5.0f);

        Assert.Equal(1, result);
    }

    [Fact]
    public void Plain_function_survives_alongside_unexpressible_default_functions()
    {
        var result = E2EBindings.CheckPlainNoDefaults(21);

        Assert.Equal(42, result);
    }
}
