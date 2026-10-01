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
        document.CloseDocument();
        Assert.True(document.Closed);
        Assert.Equal(42, document.DisposeDocument());
        Assert.True(document.IsClosedDocument);
    }

    [Fact]
    public void Explicit_rename_to_a_reserved_name_is_left_alone()
    {
        using var document = new Document();

        Assert.Null(typeof(Document).GetMethod("CloseDocument", [typeof(int)]));
        Assert.Equal(typeof(Document), typeof(Document).GetMethod("Close", [typeof(int)])!.DeclaringType);
        document.Close(9);
        Assert.Equal(9, document.CloseCode);
    }

    [Fact]
    public void Static_helper_names_are_renamed_only_when_the_signature_matches()
    {
        Assert.Null(typeof(Document).GetMethod("GetHandleDocument"));
        var method = typeof(Document).GetMethod("GetHandle", [typeof(int), typeof(int)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(Document), method!.DeclaringType);
        using var document = new Document();
        Assert.Equal(7, document.GetHandle(3, 4));
        Assert.Null(typeof(Document).GetMethod("GetObjectDocument"));
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
        document.CloseDocument();
        Assert.True(document.GetIsClosed());
    }

    [Fact]
    public void Overrides_use_the_name_of_the_root_declaring_class_and_dispatch_through_the_base()
    {
        using var window = new DerivedWindow();
        BaseWindow asBase = window;

        Assert.NotNull(typeof(DerivedWindow).GetMethod("CloseBaseWindow", Type.EmptyTypes));
        Assert.Equal(typeof(SafeHandle), typeof(DerivedWindow).GetMethod("Close", Type.EmptyTypes)!.DeclaringType);
        asBase.CloseBaseWindow();
        Assert.True(window.DerivedClosed);
        Assert.False(window.BaseClosed);
    }

    [Fact]
    public void Every_override_in_a_chain_takes_the_name_of_the_class_that_first_declares_the_virtual()
    {
        Assert.NotNull(typeof(DeepWindow).GetMethod("CloseBaseWindow", Type.EmptyTypes));
        Assert.Null(typeof(DeepWindow).GetMethod("CloseDerivedWindow"));
        Assert.Null(typeof(DeepWindow).GetMethod("CloseDeepWindow"));
        Assert.Equal(typeof(SafeHandle), typeof(DeepWindow).GetMethod("Close", Type.EmptyTypes)!.DeclaringType);

        using var window = new DeepWindow();
        BaseWindow asBase = window;
        asBase.CloseBaseWindow();
        Assert.True(window.DeepClosed);
        Assert.False(window.DerivedClosed);
        Assert.False(window.BaseClosed);
    }

    [Fact]
    public void Non_virtual_function_whose_replacement_name_is_taken_takes_a_counter()
    {
        Assert.NotNull(typeof(CounterWindow).GetMethod("CloseCounterWindow2", Type.EmptyTypes));
        Assert.Single(typeof(CounterWindow).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            static method => method.Name == "CloseCounterWindow");
        using var window = new CounterWindow();
        window.CloseCounterWindow2();
        window.CloseCounterWindow();
        Assert.True(window.Closed);
        Assert.True(window.CounterCloseCalled);
    }

    [Fact]
    public void Virtual_function_whose_replacement_name_is_taken_is_ignored()
    {
        var nativeCloses = typeof(TakenWindow).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name == "CloseTakenWindow").ToArray();
        Assert.Single(nativeCloses);
        using var window = new TakenWindow();
        window.CloseTakenWindow();
        Assert.True(window.TakenCloseCalled);
        Assert.False(window.VirtualCloseCalled);
    }

    [Fact]
    public void Non_virtual_overload_set_shares_one_replacement_name()
    {
        Assert.NotNull(typeof(OverloadedWindow).GetMethod("CloseOverloadedWindow", Type.EmptyTypes));
        Assert.NotNull(typeof(OverloadedWindow).GetMethod("CloseOverloadedWindow", [typeof(int)]));
        Assert.Null(typeof(OverloadedWindow).GetMethod("CloseOverloadedWindow2"));
        using var window = new OverloadedWindow();
        window.CloseOverloadedWindow();
        window.CloseOverloadedWindow(7);
        Assert.True(window.Closed);
        Assert.Equal(7, window.LastCode);
    }

    [Fact]
    public void Virtual_function_keeps_its_replacement_when_a_non_virtual_overload_exists()
    {
        Assert.NotNull(typeof(MixedWindow).GetMethod("CloseMixedWindow", Type.EmptyTypes));
        Assert.NotNull(typeof(MixedWindow).GetMethod("CloseMixedWindow", [typeof(int)]));
        Assert.Null(typeof(MixedWindow).GetMethod("CloseMixedWindow2"));
        using var window = new MixedWindow();
        window.CloseMixedWindow();
        window.CloseMixedWindow(3);
        Assert.True(window.VirtualCloseCalled);
        Assert.Equal(3, window.LastCode);
    }

    [Fact]
    public void Static_helper_signature_match_is_renamed_and_stays_callable()
    {
        var instance = typeof(OpaqueWindow).GetMethod("GetHandleOpaqueWindow", [typeof(object)]);
        Assert.NotNull(instance);
        Assert.Equal(typeof(OpaqueWindow), instance!.DeclaringType);
        Assert.False(instance.IsStatic);
        Assert.Equal(typeof(OpaqueWindow), typeof(OpaqueWindow).GetMethod("GetObjectOpaqueWindow", [typeof(object)])!.DeclaringType);
        Assert.Equal(typeof(OpaqueWindow), typeof(OpaqueWindow).GetMethod("TransferHandleOwnershipOpaqueWindow", [typeof(object)])!.DeclaringType);

        // The inherited static helpers are not hidden.
        foreach (var helper in new[] { "GetHandle", "GetObject", "TransferHandleOwnership" })
        {
            var inherited = typeof(OpaqueWindow).GetMethod(helper, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, [typeof(object)]);
            Assert.Equal(typeof(NativeObject), inherited!.DeclaringType);
            Assert.Null(typeof(OpaqueWindow).GetMethod(helper, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, [typeof(object)]));
        }

        using var window = new OpaqueWindow();
        Assert.Equal(42, window.GetHandleOpaqueWindow(21));
        Assert.Equal(107, OpaqueWindow.GetObjectOpaqueWindow(7));
        Assert.Equal(4, OpaqueWindow.TransferHandleOwnershipOpaqueWindow(5));
    }

    [Fact]
    public void Generator_log_reports_the_rename_and_the_refused_property()
    {
        var lines = Diagnostics.GeneratedArtifacts.ReadText("generator.log").Split('\n');

        Assert.Contains(lines, static line => line.StartsWith("WARNING\tFunction\t", StringComparison.Ordinal) &&
            line.Contains("document::close", StringComparison.Ordinal) &&
            line.Contains("CloseDocument", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("WARNING\tFunction\t", StringComparison.Ordinal) &&
            line.Contains("document::get_is_closed", StringComparison.Ordinal) &&
            line.Contains("'IsClosed'", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("WARNING\tFunction\t", StringComparison.Ordinal) &&
            line.Contains("opaque_window::get_handle", StringComparison.Ordinal) &&
            line.Contains("GetHandleOpaqueWindow", StringComparison.Ordinal));
    }

    [Fact]
    public void Rename_on_a_projected_record_uses_the_record_name_not_the_projected_managed_type()
    {
        var lines = Diagnostics.GeneratedArtifacts.ReadText("generator.log").Split('\n');

        var warnings = lines.Where(static line => line.StartsWith("WARNING\tFunction\t", StringComparison.Ordinal) &&
            line.Contains("opaque_token::is_invalid", StringComparison.Ordinal)).ToArray();
        Assert.Single(warnings);
        Assert.Contains("'IsInvalidOpaqueToken'", warnings[0], StringComparison.Ordinal);
        Assert.DoesNotContain("IsInvalidobject", warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Overload_set_with_virtuals_rooted_in_different_records_is_ignored()
    {
        var lines = Diagnostics.GeneratedArtifacts.ReadText("generator.log").Split('\n');

        Assert.Contains(lines, static line => line.Contains("split_window::close", StringComparison.Ordinal) &&
            line.Contains("different records", StringComparison.Ordinal));

        var instanceMethods = typeof(SplitWindow).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(instanceMethods, static method => method.Name is "CloseBaseWindow" or "CloseSplitWindow");
        // The only CloseOtherBase is the IOtherBase forwarder for other_base::close(int); split_window::close(int) is not bound.
        var closeOtherBase = Assert.Single(instanceMethods, static method => method.Name == "CloseOtherBase");
        Assert.Equal([typeof(int)], closeOtherBase.GetParameters().Select(static parameter => parameter.ParameterType));
    }
}
