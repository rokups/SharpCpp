using SharpCpp.Tests.E2E.Modules.OwnedFiles.Generated;
using SharpCpp.Tests.E2E.Modules.OwnedFilesTrailingSeparator.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Modules.OwnedFiles;

public sealed class OwnedRootTests
{
    [Fact]
    public void Owned_header_roots_generate_compiled_native_round_trips()
    {
        Assert.Equal(42, OwnedBindings.OwnedRootFirst(41));
        Assert.Equal(43, OwnedBindings.OwnedRootSecond(41));
    }

    [Fact]
    public void Owned_header_root_with_trailing_separator_owns_the_same_files()
    {
        Assert.Equal(42, OwnedTrailingSeparatorBindings.OwnedFirstRoundTrip(41));
        Assert.Equal(43, OwnedTrailingSeparatorBindings.OwnedSecondRoundTrip(41));
    }

    [Fact]
    public void Owned_alias_wraps_an_external_enum_under_the_alias_name()
    {
        var next = OwnedBindings.OwnedNextColor(Modules.OwnedFiles.Generated.OwnedColor.ExternalColorRed);
        Assert.Equal(Modules.OwnedFiles.Generated.OwnedColor.ExternalColorGreen, next);
        Assert.Equal(typeof(byte), System.Enum.GetUnderlyingType(typeof(Modules.OwnedFiles.Generated.OwnedColor)));
    }

    [Fact]
    public void External_enum_declared_by_a_macro_keeps_its_fixed_underlying_type()
    {
        Assert.Equal(typeof(ushort), System.Enum.GetUnderlyingType(typeof(Modules.OwnedFiles.Generated.OwnedShade)));
        Assert.Equal(sizeof(ushort), System.Runtime.CompilerServices.Unsafe.SizeOf<Modules.OwnedFiles.Generated.OwnedShade>());
        var darker = OwnedBindings.OwnedDarkerShade(Modules.OwnedFiles.Generated.OwnedShade.ExternalShadeLight);
        Assert.Equal(Modules.OwnedFiles.Generated.OwnedShade.ExternalShadeDark, darker);
    }
}
