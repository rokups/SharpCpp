using System.Collections.Generic;

using Cross_app;
using Cross_library;

using SharpCpp.Tests.E2E.Modules.CrossModule.App;

using Xunit;

namespace SharpCpp.Tests.E2E.Modules.CrossModule;

/// <summary>A static interface that the Library module declares for wrappers derived from library_widget.</summary>
public interface IImportedStaticTag<TSelf>
    where TSelf : class
{
}

public sealed class CrossModuleTests
{
    private static readonly System.Text.Json.JsonElement LibraryDocument = System.Text.Json.JsonDocument.Parse(
        global::SharpCpp.Tests.E2E.Diagnostics.GeneratedArtifacts.ReadText("SharpCppCrossModuleLibrary.Bindings.json")).RootElement;

    [Fact]
    public void Static_interfaces_round_trip_through_exported_metadata()
    {
        var widget = LibraryDocument.GetProperty("types").EnumerateArray()
            .Single(static type => type.GetProperty("symbol_id").GetString() == "record:cross_library::library_widget");
        Assert.Equal(
            new[] { "global::SharpCpp.Tests.E2E.Modules.CrossModule.IImportedStaticTag<{managed_type}>" },
            widget.GetProperty("managed_static_interfaces").EnumerateArray().Select(static item => item.GetString()).ToArray());
        Assert.True(widget.GetProperty("managed_static_interfaces_abstract").GetBoolean());

        // The abstract flag keeps the interface off the library wrapper; the importing module
        // applies the imported interface to its derived wrapper.
        Assert.DoesNotContain(typeof(IImportedStaticTag<LibraryWidget>), typeof(LibraryWidget).GetInterfaces());
        Assert.Contains(typeof(IImportedStaticTag<AppWidget>), typeof(AppWidget).GetInterfaces());
    }

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
    public void Imported_records_returned_by_pointer_and_reference_are_handles()
    {
        using var widget = new AppWidget();
        var sameWidget = AppBindings.AppSameWidget(widget);
        Assert.Equal(global::SharpCpp.Support.NativeObject.GetHandle(widget), global::SharpCpp.Support.NativeObject.GetHandle(sameWidget));
        Assert.Equal("widget", sameWidget.Describe().ToString());

        using var settings = new LibrarySettings();
        settings.Values = new global::Std.Vector<int>(new[] { 4, 5 });
        var sameSettings = AppBindings.AppSameSettings(settings);
        Assert.Equal(global::SharpCpp.Support.NativeObject.GetHandle(settings), global::SharpCpp.Support.NativeObject.GetHandle(sameSettings));
        Assert.Equal(9, sameSettings.Total());
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
    public void Same_native_type_spelled_differently_resolves_one_provider_shape()
    {
        using var scores = new global::Std.UnorderedMap<int, float>();
        scores.Add(1, 2.5f);

        Assert.Equal(2.5f, global::SharpCpp.Tests.E2E.Modules.CrossModule.Library.LibraryBindings.LibraryScore(scores, 1));
        Assert.Equal(1, AppBindings.AppScoreCount(scores));
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

    [Fact]
    public void Aliases_of_unprojectable_generic_arguments_are_pruned()
    {
        var aliases = Diagnostics.GeneratedArtifacts.ReadText("SharpCppCrossModuleLibrary.Bindings.Aliases.g.cs");
        Assert.Contains("global using ScoreMap = ", aliases);
        Assert.DoesNotContain("EnabledPasses", aliases);
    }

    [Fact]
    public void Defaulted_nested_template_arguments_do_not_split_a_provider_shape()
    {
        using var values = new global::Std.UnorderedMap<int, double>();
        values[1] = 2.5;
        values[2] = 4;
        Assert.Equal(6.5, Library.LibraryBindings.LibrarySum(values));
        Assert.Equal(6.5, Library.LibraryBindings.LibrarySumWritten(values));
    }

    [Fact]
    public void Function_template_instantiations_keep_aliased_callback_parameters()
    {
        using var events = new LibraryEvents();
        var seen = 0;
        Assert.Equal(2, events.Subscribe(scores => { seen = scores.Count; scores[3] = 1; }));
        Assert.Equal(1, seen);
        Assert.Contains(
            "global::System.Action<ScoreMap>",
            Diagnostics.GeneratedArtifacts.ReadText("SharpCppCrossModuleLibrary.Bindings.g.cs"));
    }
}
