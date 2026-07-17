using System.IO;
using System.Xml.Linq;

namespace Companion.RenderHarness.Tests;

/// <summary>Static XAML guard for degraded save slots. The Save Manager owns a live session and
/// shell, so its data-template state is guarded without opening or mutating a career database.</summary>
public sealed class SaveManagerWindowContractTests
{
    [Fact]
    public void DegradedSaveSlot_IsMarkedRecovered_HidesUntrustedMetadata_AndRemainsRestorable()
    {
        XDocument document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "Companion.App", "Views", "SaveManagerWindow.xaml"));

        XElement recovered = Assert.Single(document.Descendants(),
            element =>
                element.Name.LocalName == "TextBlock" &&
                Attribute(element, "Text") == "RECOVERED");
        Assert.NotNull(recovered.Parent);

        XElement explanation = Assert.Single(document.Descendants(),
            element =>
                element.Name.LocalName == "TextBlock" &&
                Attribute(element, "Text")?.Contains(
                    "metadata was lost", StringComparison.Ordinal) == true);
        Assert.Contains("IsDegraded", Attribute(explanation, "Visibility"), StringComparison.Ordinal);
        Assert.Contains("BoolVisible", Attribute(explanation, "Visibility"), StringComparison.Ordinal);

        XElement[] hiddenMetadata = document.Descendants()
            .Where(element =>
                element.Name.LocalName == "TextBlock" &&
                Attribute(element, "Visibility")?.Contains("IsDegraded", StringComparison.Ordinal) == true &&
                Attribute(element, "Visibility")?.Contains("BoolCollapsed", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.True(hiddenMetadata.Length >= 2);

        XElement restore = Assert.Single(document.Descendants(),
            element =>
                element.Name.LocalName == "Button" &&
                Attribute(element, "Content") == "RESTORE");
        Assert.DoesNotContain(restore.Attributes(),
            attribute => attribute.Name.LocalName == "IsEnabled");
        Assert.DoesNotContain(restore.DescendantsAndSelf().Attributes(),
            attribute => attribute.Value.Contains("IsDegraded", StringComparison.Ordinal));
    }

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Companion.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not find Companion.slnx above '{AppContext.BaseDirectory}'.");
    }
}
