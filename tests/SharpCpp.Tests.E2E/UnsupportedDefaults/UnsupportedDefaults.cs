using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.UnsupportedDefaults;

public sealed class UnsupportedDefaultsTests
{
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
    public void Void_handle_and_colliding_constructor_defaults_round_trip_native_dispatch()
    {
        E2EBindings.SetUnsupportedDefaultState();
        Assert.Equal(4, E2EBindings.GetUnsupportedDefaultState());
        E2EBindings.SetUnsupportedDefaultState(9);
        Assert.Equal(9, E2EBindings.GetUnsupportedDefaultState());

        using var defaultResult = E2EBindings.MakeUnsupportedDefaultResult();
        using var explicitResult = E2EBindings.MakeUnsupportedDefaultResult(11);
        Assert.Equal(4, defaultResult.GetValue());
        Assert.Equal(11, explicitResult.GetValue());

        using var payload = new UnsupportedDefaultConstructorPayload(13);
        using var exactConstructor = new UnsupportedDefaultConstructorCollision(payload);
        using var defaultedConstructor = new UnsupportedDefaultConstructorCollision(payload, null, out var defaultObserved);
        using var explicitConstructor = new UnsupportedDefaultConstructorCollision(payload, 8, out var observed);
        Assert.Equal(13, exactConstructor.GetValue());
        Assert.Equal(17, defaultedConstructor.GetValue());
        Assert.Equal(0, defaultObserved);
        Assert.Equal(21, explicitConstructor.GetValue());
        Assert.Equal(22, observed);
    }

    [Fact]
    public void Omitted_and_explicit_output_defaults_round_trip_native_dispatch()
    {
        Assert.Equal(16, E2EBindings.UnsupportedDefaultWithOutput(11));
        Assert.Equal(19, E2EBindings.UnsupportedDefaultWithOutput(11, 7, 0, out var output));
        Assert.Equal(18, output);

        E2EBindings.SetUnsupportedDefaultWithOutput(20);
        Assert.Equal(24, E2EBindings.GetUnsupportedDefaultState());
        E2EBindings.SetUnsupportedDefaultWithOutput(20, 8, out var explicitOutput);
        Assert.Equal(28, E2EBindings.GetUnsupportedDefaultState());
        Assert.Equal(29, explicitOutput);
    }

    [Fact]
    public void Mixed_default_free_function_omits_unexpressible_suffix_at_native_boundary()
    {
        Assert.Equal(1445, E2EBindings.CheckMixedDefaults(1, 4));
        Assert.Equal(1465, E2EBindings.CheckMixedDefaults(1, 4, 6));
        Assert.Equal(1467, E2EBindings.CheckMixedDefaults(1, 4, 6, 7));
    }

    [Fact]
    public void Alternating_defaults_omit_only_unexpressible_suffixes_at_native_boundary()
    {
        Assert.Equal(128765, E2EBindings.CheckAlternatingDefaults(1, 2));
        Assert.Equal(129765, E2EBindings.CheckAlternatingDefaults(1, 2, 9));
        Assert.Equal(129365, E2EBindings.CheckAlternatingDefaults(1, 2, 9, 3));
        Assert.Equal(129345, E2EBindings.CheckAlternatingDefaults(1, 2, 9, 3, 4));
        Assert.Equal(129342, E2EBindings.CheckAlternatingDefaults(1, 2, 9, 3, 4, 2));
    }

    [Fact]
    public void Mixed_default_constructors_and_methods_omit_unexpressible_suffix_at_native_boundary()
    {
        using var defaultCounter = new UnsupportedDefaultCounter(3, 2);
        Assert.Equal(3245, defaultCounter.GetValue());
        Assert.Equal(4607, defaultCounter.Combine(1, 3));

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

    [Fact]
    public void Small_enum_constructor_defaults_use_the_widened_abi()
    {
        using var defaulted = new UnsupportedDefaultEnumBox();
        using var explicitValue = new UnsupportedDefaultEnumBox(UnsupportedDefaultMode.High);

        Assert.Equal(UnsupportedDefaultMode.High, defaulted.GetMode());
        Assert.Equal(UnsupportedDefaultMode.High, explicitValue.GetMode());
    }

    [Fact]
    public void Record_pointer_default_before_callback_default_accepts_null_and_a_value()
    {
        using var item = new UnsupportedDefaultItem { Value = 5 };

        Assert.Equal(9, E2EBindings.UnsupportedDefaultRecordPointerThenCallback(10));
        Assert.Equal(9, E2EBindings.UnsupportedDefaultRecordPointerThenCallback(10, null));
        Assert.Equal(15, E2EBindings.UnsupportedDefaultRecordPointerThenCallback(10, item));
        Assert.Equal(90, E2EBindings.UnsupportedDefaultRecordPointerThenCallback(10, null, value => value * 10));
        Assert.Equal(150, E2EBindings.UnsupportedDefaultRecordPointerThenCallback(10, item, value => value * 10));
    }

    [Fact]
    public void Vector_pointer_default_before_callback_default_accepts_null_and_a_value()
    {
        using var values = new global::Std.Vector<int>((IEnumerable<int>)new[] { 1, 2, 3 });

        Assert.Equal(9, E2EBindings.UnsupportedDefaultVectorPointerThenCallback(10));
        Assert.Equal(9, E2EBindings.UnsupportedDefaultVectorPointerThenCallback(10, null));
        Assert.Equal(13, E2EBindings.UnsupportedDefaultVectorPointerThenCallback(10, values));
        Assert.Equal(90, E2EBindings.UnsupportedDefaultVectorPointerThenCallback(10, null, value => value * 10));
        Assert.Equal(130, E2EBindings.UnsupportedDefaultVectorPointerThenCallback(10, values, value => value * 10));
    }

    [Fact]
    public void Consecutive_null_default_record_pointers_before_callback_default_accept_every_combination()
    {
        using var a = new UnsupportedDefaultItem { Value = 2 };
        using var b = new UnsupportedDefaultItem { Value = 3 };

        Assert.Equal(1, E2EBindings.UnsupportedDefaultTwoRecordPointersThenCallback(1));
        Assert.Equal(1, E2EBindings.UnsupportedDefaultTwoRecordPointersThenCallback(1, null, null));
        Assert.Equal(301, E2EBindings.UnsupportedDefaultTwoRecordPointersThenCallback(1, null, b));
        Assert.Equal(21, E2EBindings.UnsupportedDefaultTwoRecordPointersThenCallback(1, a, null));
        Assert.Equal(321, E2EBindings.UnsupportedDefaultTwoRecordPointersThenCallback(1, a, b));

        Assert.Equal(2, E2EBindings.UnsupportedDefaultTwoRecordPointersThenCallback(1, null, null, value => value * 2));
        Assert.Equal(602, E2EBindings.UnsupportedDefaultTwoRecordPointersThenCallback(1, null, b, value => value * 2));
        Assert.Equal(42, E2EBindings.UnsupportedDefaultTwoRecordPointersThenCallback(1, a, null, value => value * 2));
        Assert.Equal(642, E2EBindings.UnsupportedDefaultTwoRecordPointersThenCallback(1, a, b, value => value * 2));
    }

    [Fact]
    public void Null_default_callback_before_null_default_pointer_accepts_every_combination()
    {
        using var item = new UnsupportedDefaultItem { Value = 2 };

        Assert.Equal(5, E2EBindings.UnsupportedDefaultCallbackThenPointer(1));
        Assert.Equal(5, E2EBindings.UnsupportedDefaultCallbackThenPointer(1, null, null));
        Assert.Equal(25, E2EBindings.UnsupportedDefaultCallbackThenPointer(1, null, item));
        Assert.Equal(10, E2EBindings.UnsupportedDefaultCallbackThenPointer(1, value => value * 2, null));
        Assert.Equal(50, E2EBindings.UnsupportedDefaultCallbackThenPointer(1, value => value * 2, item));
        Assert.Equal(54, E2EBindings.UnsupportedDefaultCallbackThenPointer(1, value => value * 2, item, 6));
    }
}
