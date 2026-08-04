using Parser_breadth;
using SharpCpp.Tests.E2E.Generated;
using Xunit;

namespace SharpCpp.Tests.E2E.CoverageMatrix;

public sealed class ParserBreadthTests
{
    [Fact]
    public void Dense_parser_surface_keeps_supported_native_round_trips()
    {
        Assert.Equal(44, E2EBindings.ParserBreadthRoundTrip(41));
        Assert.Equal(38, E2EBindings.EvaluatedBreadthDefaults(0));
        Assert.Equal(0, E2EBindings.InvokeBreadthRawCallback(0));

        using var payload = new BreadthPayload(17);
        Assert.Same(payload, E2EBindings.BreadthAliasReference(payload));
        Assert.Same(payload, E2EBindings.BreadthProjectPayload(payload));
        Assert.Same(payload, E2EBindings.BreadthProjectNullablePayload(payload));
        Assert.Null(E2EBindings.BreadthProjectNullablePayload(null));
        Assert.Equal(BreadthMode.Active, E2EBindings.BreadthProjectMode(BreadthMode.Active));

        using var late = new BreadthLateRecord(23);
        Assert.Equal(23, E2EBindings.ReadBreadthLateRecord(late));

        using var outOfLineNested = E2EBindings.MakeBreadthOutOfLineNested(27);
        Assert.Equal(27, outOfLineNested.Read());

        using var box = new BreadthForwardBox<int>(29);
        Assert.Equal(29, box.GetValue());
        Assert.Same(box, E2EBindings.BreadthRoundtripBox(box));
        Assert.Equal(29, box.Echo(box.GetValue()));

        using var payloadBox = new BreadthForwardBox<BreadthPayload>(payload);
        Assert.Same(payload, payloadBox.GetValue());
        Assert.Same(payloadBox, E2EBindings.BreadthRoundtripBox(payloadBox));
        Assert.Same(payload, payloadBox.Echo(payload));
        Assert.Same(payload, payloadBox.EchoPayload(payload));
        Assert.Equal(17, payloadBox.ReadPayload(payload));
        using var returnedPayloadBox = E2EBindings.MakeBreadthPayloadBox(payload);
        Assert.Same(payload, E2EBindings.ReadBreadthPayloadBox(returnedPayloadBox));
        using var aliasPayloadBox = E2EBindings.MakeBreadthAliasPayloadBox(payload);
        Assert.Same(payload, E2EBindings.ReadBreadthAliasPayloadBox(aliasPayloadBox));

        Assert.Equal(31, E2EBindings.BreadthTemplateIdentity(31));
        Assert.Same(payload, E2EBindings.BreadthTemplateIdentity(payload));
        Assert.Equal(BreadthMode.Active, E2EBindings.BreadthTemplateIdentity(BreadthMode.Active));
        Assert.Equal(53, E2EBindings.BreadthUnnamedIdentity(47));

        var incremented = 40;
        E2EBindings.BreadthIncrementTemplate(ref incremented);
        Assert.Equal(41, incremented);
        Assert.Equal(42, E2EBindings.InvokeBreadthCallbackReference(value => value + 1, 40));

        using var nestedEnum = new BreadthNestedEnumOwner(BreadthNestedMode.Second);
        Assert.Equal(BreadthNestedMode.Second, nestedEnum.GetValue());

        using var objectNames = new BreadthObjectNames(11);
        using var equalObjectNames = new BreadthObjectNames(11);
        Assert.Equal(12, objectNames.GetType());
        Assert.Equal(13, objectNames.GetHashCode());
        Assert.True(objectNames.Equals(equalObjectNames));

        using var namedDerived = new BreadthNamedDerived();
        Assert.Equal(43, namedDerived.Ping(41));

        using var anonymousOwner = new BreadthEmptyAnonymousOwner(59);
        Assert.Equal(59, anonymousOwner.Read());

        using var unnamedMembers = new BreadthUnnamedMembers(0);
        Assert.Equal(61, unnamedMembers.Read());
        Assert.Equal(42, BreadthUnnamedMembers.Combine(0, 41));

        using var flattened = new BreadthFlattenedOwner();
        flattened.GroupLeafVisible = 39;
        flattened.GroupText = 0;
        flattened.GroupExternal = 0;
        Assert.Equal(42, E2EBindings.ReadBreadthFlattenedOwner(flattened));

        Assert.Equal(376, E2EBindings.BreadthLiteralDefaults());
        Assert.Equal(351, E2EBindings.BreadthLiteralDefaults(1.0f, 2.5, BreadthPlainMode.BreadthPlainActive));
        Assert.Equal(8, E2EBindings.BreadthQualifiedDefault());
        Assert.Equal(42, E2EBindings.BreadthQualifiedDefault(41));
        Assert.Equal(10, E2EBindings.BreadthNamedIdentifierDefault());
        Assert.Equal(42, E2EBindings.BreadthNamedIdentifierDefault(41));

        using var alias = E2EBindings.MakeParserAliasPayload(37);
        Assert.Equal(37, E2EBindings.ReadParserAliasPayload(alias));
    }
}
