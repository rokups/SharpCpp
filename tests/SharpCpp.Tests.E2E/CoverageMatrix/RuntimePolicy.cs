using SharpCpp.Support;
using SharpCpp.Tests.E2E.Generated;
using Xunit;

namespace SharpCpp.Tests.E2E.CoverageMatrix;

public abstract class CoveragePolicyNativeObject : NativeObject
{
    protected CoveragePolicyNativeObject(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }
}

public static class CoveragePolicyCache
{
    public static INativeInstanceCache Shared { get; } = new DefaultNativeInstanceCache();
}

public interface ICoverageStaticTypeInfo<TSelf>
{
}

public sealed class RuntimePolicyTests
{
    [Fact]
    public void Configured_runtime_policy_is_inherited_by_generated_native_wrappers()
    {
        using var baseValue = new RuntimePolicyBase(31);
        Assert.Equal(31, baseValue.GetValue());

        using var derived = E2EBindings.GetRuntimePolicyDerived(41);
        Assert.Equal(42, derived.GetValue());
        Assert.Equal(73, RuntimePolicyDerived.GetTypeStatic());
    }

    [Fact]
    public void Rename_and_scalar_marshaller_configuration_round_trip_native_values()
    {
        using var renamed = new RuntimeRenamedValue(17);
        Assert.Equal(17, renamed.GetValue());

        Assert.Equal(29, E2EBindings.MakeRuntimeScalar(29));
        Assert.Equal(38, E2EBindings.ConsumeRuntimeScalar(37));
    }
}
