using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class BorrowedDirectorBreadthTests
{
    private sealed class ManagedBorrowedDirectorSource : BorrowedDirectorSource
    {
        private readonly BorrowedDirectorPayload _selected;

        public ManagedBorrowedDirectorSource(BorrowedDirectorPayload selected)
        {
            _selected = selected;
        }

        public int CallbackCount { get; private set; }

        public override BorrowedDirectorPayload ChoosePayload(BorrowedDirectorPayload fallback)
        {
            CallbackCount++;
            Assert.Equal(19, fallback.GetValue());
            return _selected;
        }
    }

    [Fact]
    public void Borrowed_handle_return_from_managed_director_is_read_by_native_code()
    {
        using var fallback = new BorrowedDirectorPayload(19);
        using var selected = new BorrowedDirectorPayload(42);
        using var source = new ManagedBorrowedDirectorSource(selected);

        Assert.Equal(42, source.ReadChosenPayload(fallback));
        Assert.Equal(1, source.CallbackCount);
        Assert.Equal(42, selected.GetValue());
    }
}
