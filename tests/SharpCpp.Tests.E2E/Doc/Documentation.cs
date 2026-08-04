using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Doc;

public sealed class DocumentationTests
{
    [Fact]
    public void Documented_declarations_round_trip_through_generated_native_bindings()
    {
        using var record = new DocumentedRecord(11);
        Assert.Equal(11, record.GetValue());
        record.StoredValue = 17;
        Assert.Equal(42, record.Increment(25));
        record.SetValue(23);
        Assert.Equal(23, record.GetValue());

        Assert.Equal(42, DocumentedRecord.Reset());
        Assert.Equal(42, E2EBindings.DocumentedFreeFunction(21));
        Assert.Equal(DocumentedStatus.Pending, E2EBindings.RoundtripDocumentedStatus(DocumentedStatus.Pending));
        Assert.Equal(42, E2EBindings.FunctionWithXmlSensitiveComment());
    }
}
