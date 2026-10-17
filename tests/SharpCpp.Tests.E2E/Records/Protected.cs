using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed partial class ProtectedTests
{
    private sealed partial class ManagedProtectedCounter : ProtectedCounter
    {
        public ManagedProtectedCounter(int seed)
            : base(seed)
        {
        }

        public int CallbackCount { get; private set; }

        public int CallIncrementFromManaged(int amount)
        {
            return Increment(amount);
        }

        public int ReadFactorFromManaged()
        {
            return Factor;
        }

        public void WriteFactorFromManaged(int value)
        {
            Factor = value;
        }

        public int[] ReadPayloadsFromManaged()
        {
            return Payloads.Select(payload => payload.GetValue()).ToArray();
        }

        public void WritePayloadFromManaged(int index, ProtectedPayload value)
        {
            Payloads[index] = value;
        }

        protected override int Scale(int value)
        {
            CallbackCount++;
            return GetSeed() + (Factor * 10) + value;
        }
    }

    [Fact]
    public void Managed_subclasses_can_use_protected_methods_fields_and_virtuals()
    {
        using var counter = new ManagedProtectedCounter(4);

        Assert.Equal(2, counter.ReadFactorFromManaged());
        Assert.Equal(7, counter.CallIncrementFromManaged(3));
        Assert.Equal(7, counter.GetSeed());
        Assert.Equal(new[] { 4, 5 }, counter.ReadPayloadsFromManaged());

        using var replacement = new ProtectedPayload(11);
        counter.WritePayloadFromManaged(1, replacement);
        Assert.Equal(15, counter.SumPayloads());

        counter.WriteFactorFromManaged(5);

        Assert.Equal(5, counter.ReadFactorFromManaged());
        Assert.Equal(58, counter.CallScale(1));
        Assert.Equal(1, counter.CallbackCount);
    }

    [Fact]
    public void Non_director_public_method_uses_native_protected_state()
    {
        using var counter = new PlainProtectedCounter();
        Assert.Equal(21, counter.VisibleValue());
    }

    [Fact]
    public void Protected_state_of_a_flattened_member_record_is_not_exposed()
    {
        using var holder = new GuardedValueHolder();
        Assert.Equal(9, holder.Visible);
        Assert.DoesNotContain(
            typeof(GuardedValueHolder).GetProperties(),
            static property => property.Name.Contains("Value", System.StringComparison.Ordinal));
    }
}
