namespace SharpCpp.Rendering;

public sealed class GeneratedBindingSet
{
    public GeneratedBindingSet(IReadOnlyList<GeneratedBindingFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Files = files;
    }

    public IReadOnlyList<GeneratedBindingFile> Files { get; }

    public void WriteToDirectory(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        foreach (var file in Files)
        {
            var outputPath = Path.Combine(outputDirectory, file.RelativePath);
            var parentDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            File.WriteAllText(outputPath, file.Content);
        }
    }
}
