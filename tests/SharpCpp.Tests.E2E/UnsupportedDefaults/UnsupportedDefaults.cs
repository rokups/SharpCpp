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
            BindingFlags.Public | BindingFlags.Static,
            [typeof(float), typeof(float?)])!;

        var thresholdParam = method.GetParameters()[1];

        Assert.Equal("threshold", thresholdParam.Name);
        Assert.Equal(typeof(float?), thresholdParam.ParameterType);
        Assert.False(thresholdParam.HasDefaultValue);

        Assert.NotNull(typeof(E2EBindings).GetMethod(
            nameof(E2EBindings.CheckThresholdSingleDefault),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(float)]));
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
            BindingFlags.Public | BindingFlags.Static,
            [typeof(float), typeof(float?), typeof(float?)])!;

        var aParam = method.GetParameters()[1];
        var bParam = method.GetParameters()[2];

        Assert.Equal(typeof(float?), aParam.ParameterType);
        Assert.Equal(typeof(float?), bParam.ParameterType);
        Assert.False(aParam.HasDefaultValue);
        Assert.False(bParam.HasDefaultValue);

        Assert.NotNull(typeof(E2EBindings).GetMethod(
            nameof(E2EBindings.CheckTwoDefaultsCascade),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(float)]));
        Assert.NotNull(typeof(E2EBindings).GetMethod(
            nameof(E2EBindings.CheckTwoDefaultsCascade),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(float), typeof(float?)]));
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

    [Fact]
    public void Mixed_default_signatures_keep_full_nullable_shape_and_shorter_convenience_overloads()
    {
        var freeFunction = typeof(E2EBindings).GetMethod(
            nameof(E2EBindings.CheckMixedDefaults),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(int), typeof(int), typeof(int?), typeof(int)])!;
        var freeFunctionParameters = freeFunction.GetParameters();

        Assert.False(freeFunctionParameters[0].HasDefaultValue);
        Assert.Equal(typeof(int), freeFunctionParameters[1].ParameterType);
        Assert.False(freeFunctionParameters[1].HasDefaultValue);
        Assert.Equal(typeof(int?), freeFunctionParameters[2].ParameterType);
        Assert.False(freeFunctionParameters[2].HasDefaultValue);
        Assert.Equal(typeof(int), freeFunctionParameters[3].ParameterType);
        Assert.Equal(5, freeFunctionParameters[3].DefaultValue);

        var freeFunctionConvenience = typeof(E2EBindings).GetMethod(
            nameof(E2EBindings.CheckMixedDefaults),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(int), typeof(int)])!;
        Assert.Equal(2, freeFunctionConvenience.GetParameters()[1].DefaultValue);

        var constructor = typeof(UnsupportedDefaultCounter).GetConstructor([typeof(int), typeof(int), typeof(int?), typeof(int)])!;
        var constructorParameters = constructor.GetParameters();

        Assert.False(constructorParameters[0].HasDefaultValue);
        Assert.False(constructorParameters[1].HasDefaultValue);
        Assert.Equal(typeof(int?), constructorParameters[2].ParameterType);
        Assert.False(constructorParameters[2].HasDefaultValue);
        Assert.Equal(5, constructorParameters[3].DefaultValue);

        var constructorConvenience = typeof(UnsupportedDefaultCounter).GetConstructor([typeof(int), typeof(int)])!;
        Assert.Equal(2, constructorConvenience.GetParameters()[1].DefaultValue);

        var combine = typeof(UnsupportedDefaultCounter).GetMethod(nameof(UnsupportedDefaultCounter.Combine), [typeof(int), typeof(int), typeof(int?), typeof(int)])!;
        var combineParameters = combine.GetParameters();

        Assert.False(combineParameters[0].HasDefaultValue);
        Assert.False(combineParameters[1].HasDefaultValue);
        Assert.Equal(typeof(int?), combineParameters[2].ParameterType);
        Assert.False(combineParameters[2].HasDefaultValue);
        Assert.Equal(2, combineParameters[3].DefaultValue);

        var combineConvenience = typeof(UnsupportedDefaultCounter).GetMethod(nameof(UnsupportedDefaultCounter.Combine), [typeof(int), typeof(int)])!;
        Assert.Equal(3, combineConvenience.GetParameters()[1].DefaultValue);

        var add = typeof(UnsupportedDefaultCounter).GetMethod(nameof(UnsupportedDefaultCounter.Add), BindingFlags.Public | BindingFlags.Static, [typeof(int), typeof(int?), typeof(int)])!;
        var addParameters = add.GetParameters();

        Assert.False(addParameters[0].HasDefaultValue);
        Assert.Equal(typeof(int?), addParameters[1].ParameterType);
        Assert.False(addParameters[1].HasDefaultValue);
        Assert.Equal(typeof(int), addParameters[2].ParameterType);
        Assert.Equal(5, addParameters[2].DefaultValue);

        Assert.NotNull(typeof(UnsupportedDefaultCounter).GetMethod(nameof(UnsupportedDefaultCounter.Add), BindingFlags.Public | BindingFlags.Static, [typeof(int)]));
    }

    [Fact]
    public void Mixed_default_free_function_omits_unexpressible_suffix_at_native_boundary()
    {
        Assert.Equal(1245, E2EBindings.CheckMixedDefaults(1));
        Assert.Equal(1445, E2EBindings.CheckMixedDefaults(1, 4));
        Assert.Equal(1465, E2EBindings.CheckMixedDefaults(1, 4, 6));
        Assert.Equal(1467, E2EBindings.CheckMixedDefaults(1, 4, 6, 7));
    }

    [Fact]
    public void Mixed_default_constructors_and_methods_omit_unexpressible_suffix_at_native_boundary()
    {
        using var defaultCounter = new UnsupportedDefaultCounter(3);
        Assert.Equal(3245, defaultCounter.GetValue());
        Assert.Equal(4607, defaultCounter.Combine(1));

        using var customFactorCounter = new UnsupportedDefaultCounter(3, 4);
        Assert.Equal(3445, customFactorCounter.GetValue());
        Assert.Equal(5007, customFactorCounter.Combine(1, 5));

        using var customOffsetCounter = new UnsupportedDefaultCounter(3, 4, 8);
        Assert.Equal(3485, customOffsetCounter.GetValue());
        Assert.Equal(5077, customOffsetCounter.Combine(1, 5, 9));
        Assert.Equal(5076, customOffsetCounter.Combine(1, 5, 9, 1));
    }

    [Fact]
    public void Mixed_default_static_methods_omit_unexpressible_suffix_at_native_boundary()
    {
        Assert.Equal(175, UnsupportedDefaultCounter.Add(1));
        Assert.Equal(195, UnsupportedDefaultCounter.Add(1, 9));
        Assert.Equal(193, UnsupportedDefaultCounter.Add(1, 9, 3));
    }
}
