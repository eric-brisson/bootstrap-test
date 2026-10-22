using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Starter.Init;

// Renders the output of "dotnet list package --format json" as a table.
internal static class PackageReport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IRenderable Build(string json)
    {
        var report = JsonSerializer.Deserialize<Report>(json, JsonOptions);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey30)
            .AddColumn("Projet")
            .AddColumn("Paquet")
            .AddColumn(new TableColumn("Version").RightAligned());

        foreach (var project in report?.Projects ?? [])
        {
            var packages = (project.Frameworks ?? [])
                .SelectMany(framework => framework.TopLevelPackages ?? [])
                .DistinctBy(package => package.Id)
                .OrderBy(package => package.Id);

            var projectName = Path.GetFileNameWithoutExtension(project.Path);
            foreach (var package in packages)
            {
                table.AddRow(
                    $"[grey]{Markup.Escape(projectName)}[/]",
                    Markup.Escape(package.Id),
                    $"[#8B9BFA]{Markup.Escape(package.ResolvedVersion)}[/]");

                // Show the project name only on its first row.
                projectName = string.Empty;
            }
        }

        return table;
    }

    // Only the fields we use from the JSON.
    private sealed record Report(List<Project>? Projects);

    private sealed record Project(string Path, List<TargetFramework>? Frameworks);

    private sealed record TargetFramework(List<Package>? TopLevelPackages);

    private sealed record Package(string Id, string ResolvedVersion);
}
