using System;

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

    [Fact]
    public unsafe void C_pointer_returns_and_parameters_keep_their_shapes()
    {
        Assert.Equal("message"u8, CBindings.CGetMessage());
        Assert.Equal(6u, ((uint*)CBindings.CGetWords())[1]);
        Assert.Equal(5, CBindings.CCompile("hello"));
    }

    [Fact]
    public unsafe void Adjacent_counts_make_pointer_parameters_arrays()
    {
        Assert.Equal(18u, CBindings.COptimize([5u, 6u, 7u], 3));

        var values = new uint[3];
        CBindings.CReadValues(values, 3);
        Assert.Equal(new uint[] { 0, 2, 4 }, values);

        var first = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8("a");
        var second = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8("b");
        try
        {
            Assert.Equal('a' + 'b', CBindings.CRegisterFlags([first, second], 2));
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(first);
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(second);
        }
    }

    [Fact]
    public void Pointer_typedef_to_an_opaque_struct_is_a_handle_type()
    {
        using var handle = CBindings.CCreateNativeHandle();
        Assert.Equal(8, CBindings.CUseNativeHandle(handle));
    }

    [Fact]
    public void Sdl_style_void_pointers_and_padded_event_unions_cross()
    {
        var context = CBindings.CGlGetCurrentContext();
        Assert.NotEqual(0, context);
        var before = CBindings.CGetFreedCount();
        CBindings.CSdlFree(context);
        CBindings.CSdlFree(0);
        Assert.Equal(before + 1, CBindings.CGetFreedCount());

        using var sdlEvent = new CSdlEvent();
        Assert.Equal(1, CBindings.CPollEvent(sdlEvent));
        Assert.Equal(0x200u, sdlEvent.Type);
    }

    [Fact]
    public void C_primitive_spellings_keep_their_managed_types()
    {
        Assert.False(CBindings.CAccept(true));
        Assert.True(CBindings.CAccept(false));
        Assert.Equal(-5, CBindings.CSigned(-5));
        Assert.Equal(250, CBindings.CUnsigned(250));
        Assert.Equal((byte)8, CBindings.CByteEcho(7));

        using var options = CBindings.CGetOptions();
        Assert.Equal(3, options.Version);
        Assert.True(options.Enabled);
        Assert.Empty(typeof(COptions).GetConstructors());

        var aliases = Diagnostics.GeneratedArtifacts.ReadText("SharpCppC.Bindings.Aliases.g.cs");
        Assert.Contains("global using CByte = byte;", aliases);
        Assert.DoesNotContain("CVoid", aliases);
    }

    [Fact]
    public void Uncounted_char_pointer_pointers_pass_one_string_and_external_records_stay_unbound()
    {
        Assert.Equal(5, CBindings.CFirstArgLength("argv0"));

        // FILE is declared by a system header, so its pointer typedef is not bound here.
        Assert.Null(typeof(CBindings).GetMethod("CFileIsNull"));
        Assert.Contains(
            Diagnostics.GeneratedArtifacts.ReadText("generator.log").Split('\n'),
            static line => line.Contains("\tc_file_is_null\t", StringComparison.Ordinal) &&
                line.Contains("defined outside headers owned by this compilation", StringComparison.Ordinal));
    }

    [Fact]
    public void Records_with_counted_opaque_handle_arrays_bind()
    {
        using var list = CBindings.CGetHandleList();
        list.HandleCount = 2;
        Assert.Equal(2u, list.HandleCount);
    }

    [Fact]
    public void Counted_opaque_handle_arrays_expose_bounds_checked_item_accessors()
    {
        using var list = CBindings.CGetPopulatedHandleList();
        Assert.Equal(2u, list.HandleCount);

        using var first = list.GetHandlesAt(0);
        using var second = list.GetHandlesAt(1);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(8, CBindings.CUseNativeHandle(first));
        Assert.Equal(-1, CBindings.CUseNativeHandle(second));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.GetHandlesAt(2));
    }

    [Fact]
    public void Counted_c_string_arrays_expose_bounds_checked_utf8_item_accessors()
    {
        using var list = CBindings.CGetNameList();
        Assert.Equal(3u, list.NameCount);
        Assert.Equal("alpha", list.GetNamesAt(0));
        Assert.Equal(string.Empty, list.GetNamesAt(1));
        Assert.Null(list.GetNamesAt(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.GetNamesAt(3));
    }
}
