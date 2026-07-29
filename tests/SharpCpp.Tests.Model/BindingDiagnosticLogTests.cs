using SharpCpp.Model.Ast;
using SharpCpp.Model.Diagnostics;
using SharpCpp.Model.Logging;
using SharpCpp.Pipeline;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class BindingDiagnosticLogTests
{
    [Fact]
    public void Writes_ignored_nodes_and_warnings_with_qualified_names()
    {
        var compilation = new BindingCompilation("Sample", "Sample", "Native", "SampleNative", []);
        var record = new RecordBinding("record", "Widget", "Widget", "Sample::Widget")
        {
            Source = new BindingSourceReference("Sample.hpp", 12, 3)
        };
        var function = new FunctionBinding(
            "function",
            "Run",
            "Run",
            BindingType.Void,
            cppQualifiedName: "Sample::Widget::Run");
        var parameter = new ParameterBinding("parameter", "callback", "callback", BindingType.Int32);

        parameter.SetSilentExclusionReason("callback\tshape\nis unsupported");
        record.SetWarning("base was dropped");
        function.Parameters.Add(parameter);
        record.Members.Add(function);
        compilation.Members.Add(record);

        using var writer = new StringWriter();

        BindingDiagnosticLog.Write(compilation, writer);

        var log = writer.ToString();
        Assert.Contains("WARNING\tRecord\tSample.hpp(12,3)\tSample::Widget\tbase was dropped", log, StringComparison.Ordinal);
        Assert.Contains("EXCLUDED\tParameter\t(unknown)\tSample::Widget::Run(callback)\tcallback\\tshape\\nis unsupported", log, StringComparison.Ordinal);
        Assert.Contains("SharpCpp binding diagnostics: 1 ignored or excluded, 1 warnings.", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_an_empty_summary_when_there_are_no_diagnostics()
    {
        var compilation = new BindingCompilation("Sample", "Sample", "Native", "SampleNative", []);
        using var writer = new StringWriter();

        BindingDiagnosticLog.Write(compilation, writer);

        Assert.Contains("SharpCpp binding diagnostics: 0 ignored or excluded, 0 warnings.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Pipeline_writes_diagnostics_after_post_validation_configuration()
    {
        var compilation = new BindingCompilation("Sample", "Sample", "Native", "SampleNative", []);
        using var writer = new StringWriter();
        SharpCppLog.Configure(fileWriter: writer);
        try
        {
            BindingCompilationBootstrapPipeline.Apply(
                compilation,
                [],
                [],
                configureAfterValidation: static bindingCompilation =>
                    bindingCompilation.SetWarning("late target diagnostic"));
        }
        finally
        {
            SharpCppLog.Configure();
        }

        Assert.Contains("WARNING\tCompilation\t(unknown)\tSample\tlate target diagnostic", writer.ToString(), StringComparison.Ordinal);
    }
}
