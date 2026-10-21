using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.VirtualAdvanced;

public sealed class VirtualRefCallbackAliasTests
{
    [Fact]
    public void Virtual_method_with_a_ref_callback_alias_compiles_and_calls_the_callback()
    {
        using var agent = new VrcAgent();
        agent.SetPositionCallback((int index, ref float position) => position += index + 0.5f);

        Assert.Equal(3.5f, agent.Apply(3, 0f));
    }
}
