using SharpCpp.Tests.E2E.Modules.C.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Modules.C;

public sealed class CTests
{
    [Fact]
    public void C_opaque_handles_typedefs_and_unions_round_trip_through_native_code()
    {
        using var handle = CBindings.CoverageCGetHandle();
        Assert.Equal(41, CBindings.CoverageCUseHandle(handle));

        var context = CBindings.CoverageCGetContext();
        Assert.NotEqual(0, context);
        Assert.Equal(42, CBindings.CoverageCUseContext(context));

        using var @event = CBindings.CoverageCMakeEvent(19, 23);
        Assert.Equal(42, CBindings.CoverageCSumEvent(@event));

        using var arguments = CBindings.CoverageCMakeArguments(43);
        Assert.Equal(43, CBindings.CoverageCReadArguments(arguments));
        Assert.Equal(43, arguments.Ray.Value);
    }

    [Fact]
    public void Anonymous_records_in_named_storage_flatten()
    {
        using var info = CBindings.CGetWindowInfo();
        info.InfoX11Window = 5;
        info.InfoX11Display = 1;
        Assert.Equal((nuint)1005, CBindings.CReadWindow(info));
    }

    [Fact]
    public void Typedefs_of_anonymous_structs_flatten_and_of_anonymous_unions_stay_values()
    {
        using var settings = CBindings.CGetSettings();
        settings.CapabilitiesEnabled = 4;
        Assert.Equal(4, CBindings.CReadSettings(settings));

        using var valueSettings = CBindings.CGetValueSettings();
        Assert.Equal(29, valueSettings.Data.Integer);
        Assert.Null(typeof(CValueSettings).GetProperty("DataInteger"));
    }

    [Fact]
    public void Anonymous_enums_are_skipped_and_named_enums_are_bound()
    {
        Assert.Equal(CNamedValue.CThirdValue, CBindings.CGetNamedValue());
        Assert.Equal([typeof(CNamedValue)], typeof(CBindings).Assembly.GetTypes()
            .Where(static type => type.IsEnum && type.Namespace == typeof(CBindings).Namespace));
    }
}
