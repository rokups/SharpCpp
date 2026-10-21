using System;

using Callback_containers;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Degradation;

public sealed class CallbackContainerTests
{
    [Fact]
    public void Containers_of_callbacks_are_not_bound_and_are_reported()
    {
        Assert.Null(typeof(E2EBindings).GetMethod("CountCallbacks"));
        Assert.Null(typeof(E2EBindings).GetMethod("MakeCallbacks"));
        Assert.Null(typeof(E2EBindings).GetMethod("CountEaCallbacks"));
        Assert.Null(typeof(E2EBindings).GetMethod("CountFixedCallbacks"));
        Assert.Null(typeof(CallbackList).GetProperty("Callbacks"));
        Assert.NotNull(typeof(CallbackList).GetProperty("Value"));

        var log = Diagnostics.GeneratedArtifacts.ReadText("generator.log").Split('\n');
        foreach (var (kind, name) in new[]
        {
            ("Function", "count_callbacks"),
            ("Function", "make_callbacks"),
            ("Function", "count_ea_callbacks"),
            ("Function", "count_fixed_callbacks"),
            ("Field", "callback_list::callbacks"),
        })
        {
            Assert.Contains(
                log,
                line => line.StartsWith($"IGNORED\t{kind}\t", StringComparison.Ordinal) &&
                    line.Contains($"\tcallback_containers::{name}\t", StringComparison.Ordinal) &&
                    line.Contains("a callback cannot be a generic argument", StringComparison.Ordinal));
        }
    }
}
