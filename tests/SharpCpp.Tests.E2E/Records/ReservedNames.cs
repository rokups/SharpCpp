using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using SharpCpp.Support;

using Reserved_names;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

/// <summary>Boxes the <c>int</c> payload of <c>opaque_token</c> so the token is bound as <c>object</c>.</summary>
[CustomMarshaller(typeof(object), MarshalMode.Default, typeof(OpaqueTokenMarshaller))]
public static class OpaqueTokenMarshaller
{
    public static nint ConvertToUnmanaged(object managed) =>
        SharpCpp.Support.IndirectValueMarshaller<int>.ConvertToUnmanaged((int)managed);

    public static object ConvertToManaged(nint unmanaged) =>
        SharpCpp.Support.IndirectValueMarshaller<int>.ConvertToManaged(unmanaged);

    public static void Free(nint unmanaged) =>
        SharpCpp.Support.IndirectValueMarshaller<int>.Free(unmanaged);
}

public sealed class ReservedNamesTests
{
    [Fact]
    public void Close_stays_the_safe_handle_member_and_the_native_method_is_renamed()
    {
        Assert.Equal(typeof(SafeHandle), typeof(Document).GetMethod("Close", Type.EmptyTypes)!.DeclaringType);

        var document = new Document();
        Assert.False(document.IsClosed);
        document.Close();
        Assert.True(document.IsClosed);
    }

    [Fact]
    public void Native_methods_run_the_native_implementation()
    {
        using var document = new Document();

        Assert.False(document.Closed);
        document.NativeClose();
        Assert.True(document.Closed);
        Assert.Equal(42, document.NativeDispose());
        Assert.True(document.NativeIsClosed());
    }

    [Fact]
    public void Explicit_rename_to_a_reserved_name_is_left_alone()
    {
        using var document = new Document();

        Assert.Null(typeof(Document).GetMethod("NativeClose", [typeof(int)]));
        Assert.Equal(typeof(Document), typeof(Document).GetMethod("Close", [typeof(int)])!.DeclaringType);
        document.Close(9);
        Assert.Equal(9, document.CloseCode);
    }

    [Fact]
    public void Static_helper_names_are_renamed_only_when_the_signature_matches()
    {
        Assert.Null(typeof(Document).GetMethod("NativeGetHandle"));
        var method = typeof(Document).GetMethod("GetHandle", [typeof(int), typeof(int)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(Document), method!.DeclaringType);
        using var document = new Document();
        Assert.Equal(7, document.GetHandle(3, 4));
        Assert.Null(typeof(Document).GetMethod("NativeGetObject"));
        Assert.Equal(typeof(Document), typeof(Document).GetMethod("GetObject", [typeof(uint)])!.DeclaringType);
        Assert.Equal(5, document.GetObject(4));
        Assert.Equal(typeof(NativeObject), typeof(Document).GetMethod("GetHandle", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, [typeof(object)])!.DeclaringType);
    }

    [Fact]
    public void Accessor_property_with_a_reserved_name_is_not_created()
    {
        Assert.Equal(typeof(SafeHandle), typeof(Document).GetProperty("IsClosed")!.DeclaringType);
        Assert.NotNull(typeof(Document).GetMethod("GetIsClosed"));
        using var document = new Document();
        document.NativeClose();
        Assert.True(document.GetIsClosed());
    }

    [Fact]
    public void Overrides_use_the_same_renamed_name_and_dispatch_through_the_base()
    {
        using var window = new DerivedWindow();
        BaseWindow asBase = window;

        Assert.NotNull(typeof(DerivedWindow).GetMethod("NativeClose", Type.EmptyTypes));
        Assert.Equal(typeof(SafeHandle), typeof(DerivedWindow).GetMethod("Close", Type.EmptyTypes)!.DeclaringType);
        asBase.NativeClose();
        Assert.True(window.DerivedClosed);
        Assert.False(window.BaseClosed);
    }

    [Fact]
    public void Virtual_function_whose_replacement_name_is_taken_is_ignored()
    {
        var nativeCloses = typeof(TakenWindow).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name == "NativeClose").ToArray();
        Assert.Single(nativeCloses);
        using var window = new TakenWindow();
        window.NativeClose();
        Assert.True(window.NativeCloseCalled);
        Assert.False(window.VirtualCloseCalled);
    }

    [Fact]
    public void Non_virtual_overload_set_shares_one_replacement_name()
    {
        Assert.NotNull(typeof(OverloadedWindow).GetMethod("NativeClose", Type.EmptyTypes));
        Assert.NotNull(typeof(OverloadedWindow).GetMethod("NativeClose", [typeof(int)]));
        Assert.Null(typeof(OverloadedWindow).GetMethod("NativeClose2"));
        using var window = new OverloadedWindow();
        window.NativeClose();
        window.NativeClose(7);
        Assert.True(window.Closed);
        Assert.Equal(7, window.LastCode);
    }

    [Fact]
    public void Virtual_function_keeps_its_replacement_when_a_non_virtual_overload_exists()
    {
        Assert.NotNull(typeof(MixedWindow).GetMethod("NativeClose", Type.EmptyTypes));
        Assert.NotNull(typeof(MixedWindow).GetMethod("NativeClose", [typeof(int)]));
        Assert.Null(typeof(MixedWindow).GetMethod("NativeClose2"));
        using var window = new MixedWindow();
        window.NativeClose();
        window.NativeClose(3);
        Assert.True(window.VirtualCloseCalled);
        Assert.Equal(3, window.LastCode);
    }

    [Fact]
    public void Static_helper_signature_match_is_renamed_and_stays_callable()
    {
        var instance = typeof(OpaqueWindow).GetMethod("NativeGetHandle", [typeof(object)]);
        Assert.NotNull(instance);
        Assert.Equal(typeof(OpaqueWindow), instance!.DeclaringType);
        Assert.False(instance.IsStatic);
        Assert.Equal(typeof(OpaqueWindow), typeof(OpaqueWindow).GetMethod("NativeGetObject", [typeof(object)])!.DeclaringType);
        Assert.Equal(typeof(OpaqueWindow), typeof(OpaqueWindow).GetMethod("NativeTransferHandleOwnership", [typeof(object)])!.DeclaringType);

        // The inherited static helpers are not hidden.
        foreach (var helper in new[] { "GetHandle", "GetObject", "TransferHandleOwnership" })
        {
            var inherited = typeof(OpaqueWindow).GetMethod(helper, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, [typeof(object)]);
            Assert.Equal(typeof(NativeObject), inherited!.DeclaringType);
            Assert.Null(typeof(OpaqueWindow).GetMethod(helper, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, [typeof(object)]));
        }

        using var window = new OpaqueWindow();
        Assert.Equal(42, window.NativeGetHandle(21));
        Assert.Equal(107, OpaqueWindow.NativeGetObject(7));
        Assert.Equal(4, OpaqueWindow.NativeTransferHandleOwnership(5));
    }

    [Fact]
    public void Generator_log_reports_the_rename_and_the_refused_property()
    {
        var lines = Diagnostics.GeneratedArtifacts.ReadText("generator.log").Split('\n');

        Assert.Contains(lines, static line => line.StartsWith("WARNING\tFunction\t", StringComparison.Ordinal) &&
            line.Contains("document::close", StringComparison.Ordinal) &&
            line.Contains("NativeClose", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("WARNING\tFunction\t", StringComparison.Ordinal) &&
            line.Contains("document::get_is_closed", StringComparison.Ordinal) &&
            line.Contains("'IsClosed'", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("WARNING\tFunction\t", StringComparison.Ordinal) &&
            line.Contains("opaque_window::get_handle", StringComparison.Ordinal) &&
            line.Contains("NativeGetHandle", StringComparison.Ordinal));
    }
}
