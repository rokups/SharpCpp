using System.Collections.Generic;

using Cross_app;
using Cross_library;

using SharpCpp.Tests.E2E.CrossModule.App;

using Xunit;

namespace SharpCpp.Tests.E2E.CrossModule;

public sealed class CrossModuleTests
{
    [Fact]
    public void Imported_records_with_standard_library_members_round_trip()
    {
        using var settings = new LibrarySettings();
        settings.Name = new global::Std.String("engine");
        settings.Values = new global::Std.Vector<int>(new[] { 2, 3, 4 });

        Assert.Equal("engine", settings.Name.ToString());
        Assert.Equal(9, settings.Total());
        Assert.Equal("engine!", AppBindings.AppDescribe(settings).ToString());
        Assert.Equal(3, AppBindings.AppCount(settings.Values));
    }

    [Fact]
    public void Importing_module_binds_its_own_standard_library_shapes()
    {
        using var counters = new global::Std.UnorderedMap<int, int>();
        counters.Add(5, 50);

        Assert.Equal(50, AppBindings.AppLookup(counters, 5));
        Assert.Equal(-1, AppBindings.AppLookup(counters, 6));
    }

    [Fact]
    public void Imported_callback_alias_is_bound_through_its_target_type()
    {
        using var widget = new AppWidget();
        var reported = new List<int>();

        AppBindings.AppReport(widget, reported.Add, 7);
        widget.Report(8);

        Assert.Equal(new[] { 7, 8 }, reported);
        Assert.Equal(9, widget.AppValue());
        Assert.Equal("widget", widget.Describe().ToString());
    }
}
