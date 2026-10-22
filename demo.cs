using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Starter.Init;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Windows consoles default to a legacy code page without symbols like ◆ and ✔.
        Console.OutputEncoding = Encoding.UTF8;

        ShowHeader();

        // Arguments skip every prompt (for scripts): starter-init <name> [folder]. The first template is used.
        var interactive = args.Length == 0;

        // The template comes first: later questions can depend on the project type.
        var template = interactive ? PromptTemplate() : Templates.All[0];
        var name = interactive ? PromptName() : args[0];

        if (!ValidName().IsMatch(name))
        {
            AnsiConsole.MarkupLine($"[red]'{Markup.Escape(name)}' n'est pas un espace de noms C# valide, par exemple Sample.Orders[/]");
            return 1;
        }

        var outputDir = interactive
            ? PromptOutputDir(name)
            : args.Length > 1 ? ToFullPath(args[1]) : DefaultOutputDir(name);

        if (outputDir is null)
        {
            AnsiConsole.MarkupLine($"[red]'{Markup.Escape(args[1])}' n'est pas un chemin valide.[/]");
            return 1;
        }

        if (Directory.Exists(Path.Combine(outputDir, "src", $"{name}.Api")))
        {
            AnsiConsole.MarkupLine($"[red]src/{name}.Api existe déjà dans {Markup.Escape(outputDir)}.[/]");
            return 1;
        }

        if (!await DotNetCli.IsTemplateInstalledAsync(template.ShortName))
        {
            AnsiConsole.MarkupLine($"[red]Le modèle '{template.ShortName}' n'est pas installé.[/]");
            AnsiConsole.MarkupLine("[grey]Depuis le dépôt du modèle, lancez : dotnet new install .[/]");
            return 1;
        }

        if (interactive && !IsEmptyFolder(outputDir)
            && !await ConfirmAsync("Le dossier n'est pas vide. Générer quand même ?"))
        {
            return 0;
        }

        // dotnet new refuses to overwrite files, and a freshly cloned repo often has its own .gitignore.
        var gitignore = Path.Combine(outputDir, ".gitignore");
        var keepGitignore = File.Exists(gitignore)
            && (!interactive || !await ConfirmAsync("Un .gitignore existe déjà. Le remplacer par celui du modèle ?"));
        var replaceGitignore = File.Exists(gitignore) && !keepGitignore;

        ShowSummary(name, template, outputDir);
        if (interactive && !await ConfirmAsync("Générer le projet ?"))
        {
            return 0;
        }

        var solution = Path.Combine(outputDir, $"{name}.sln");
        Step[] steps =
        [
            new("Génération du projet", "Projet généré", Color.FromHex("#8B9BFA"),
                ["new", template.ShortName, "--name", name, "--output", outputDir, .. (replaceGitignore ? ["--force"] : Array.Empty<string>())]),
            new("Restauration des paquets", "Paquets restaurés", Color.FromHex("#98C379"), ["restore", solution]),
            new("Compilation de la solution", "Solution compilée", Color.FromHex("#61AFEF"), ["build", solution, "--no-restore"]),
            new("Exécution des tests", "Tests réussis", Color.FromHex("#E5C07B"), ["test", solution, "--no-build"]),
        ];

        // Keeping the existing .gitignore: move it aside while generating, then put it back.
        var gitignoreBackup = gitignore + ".starter-init.bak";
        if (keepGitignore)
        {
            File.Move(gitignore, gitignoreBackup);
        }

        bool succeeded;
        try
        {
            succeeded = await RunStepsAsync(steps);
        }
        finally
        {
            if (keepGitignore)
            {
                File.Move(gitignoreBackup, gitignore, overwrite: true);
            }
        }

        if (!succeeded)
        {
            return 1;
        }

        ShowResult(name, outputDir);
        return 0;
    }

    private sealed record Step(string Running, string Done, Color Color, string[] Arguments);

    // Shows every step up front, then runs them one by one, each in its own color.
    private static async Task<bool> RunStepsAsync(IReadOnlyList<Step> steps)
    {
        var display = new StepDisplay();
        var finalLines = new List<string>();
        DotNetCli.Result? failure = null;

        // Pad labels to the longest one so the durations stay in place when a label changes.
        // The padding sits inside the color tag, because Spectre trims the description.
        var labelWidth = steps.Max(step => Math.Max(step.Done.Length, $"{step.Running} a échoué".Length));
        string Label(string text, string color) => $"[{color}]{text.PadRight(labelWidth)}[/]";

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(display.Columns)
            .StartAsync(async context =>
            {
                var tasks = steps
                    .Select(step => context.AddTask(Label(step.Running, "grey"), autoStart: false))
                    .ToList();

                for (var i = 0; i < steps.Count; i++)
                {
                    var (step, task) = (steps[i], tasks[i]);
                    var color = step.Color.ToMarkup();
                    task.Description = Label(step.Running, color);
                    task.StartTask();

                    var result = await DotNetCli.RunAsync(step.Arguments);
                    if (result.ExitCode != 0)
                    {
                        display.MarkFailed(task);
                        task.Description = Label($"{step.Running} a échoué", "red");
                        task.StopTask();
                        finalLines.Add($"[red]✘ {step.Running} a échoué[/]");
                        failure = result;
                        return;
                    }

                    task.Description = Label(step.Done, color);
                    task.StopTask();
                    finalLines.Add($"[green]✔[/] [{color}]{step.Done}[/] [grey]({task.ElapsedTime?.TotalSeconds:0.0} s)[/]");
                }
            });

        // Without a real terminal (script, redirected output), Spectre draws nothing: print the outcome instead.
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            finalLines.ForEach(AnsiConsole.MarkupLine);
        }

        // The dotnet output is printed after the live display, so it doesn't fight with the redraws.
        if (failure is not null)
        {
            AnsiConsole.WriteLine(failure.Output);
            return false;
        }

        return true;
    }

    private static void ShowHeader()
    {
        var version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0];

        AnsiConsole.MarkupLine("[#8B9BFA]◆ STARTER INIT[/]");
        AnsiConsole.MarkupLine($"[grey]Outil de création de projets · v{version}[/]");
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        AnsiConsole.MarkupLine("[bold]Créer un nouveau projet[/]");
        AnsiConsole.MarkupLine("[grey]Choisissez un modèle et un nom pour commencer.[/]");
        AnsiConsole.WriteLine();
    }

    private static string PromptName() =>
        AnsiConsole.Prompt(
            new TextPrompt<string>("[#8B9BFA]Nom[/] du projet (par exemple Sample.Orders) :")
                .Validate(name => ValidName().IsMatch(name)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Utilisez un espace de noms C# valide, par exemple Sample.Orders[/]")));

    // French yes/no keys: "o" (oui) and "n" (non).
    private static Task<bool> ConfirmAsync(string question) =>
        AnsiConsole.PromptAsync(new ConfirmationPrompt(question)
        {
            Yes = 'o',
            No = 'n',
            InvalidChoiceMessage = "[red]Répondez o (oui) ou n (non)[/]",
        });

    private static string PromptOutputDir(string name) =>
        ToFullPath(AnsiConsole.Prompt(
            new TextPrompt<string>("[#8B9BFA]Dossier[/] de destination :")
                .DefaultValue(DefaultOutputDir(name))
                .Validate(path => ToFullPath(path) is null
                    ? ValidationResult.Error("[red]Chemin invalide[/]")
                    : ValidationResult.Success())))!;

    // The current folder when it's empty (e.g. a freshly cloned repo), otherwise a new <name> subfolder.
    private static string DefaultOutputDir(string name)
    {
        var current = Directory.GetCurrentDirectory();
        return IsEmptyFolder(current) ? current : Path.Combine(current, name);
    }

    // Relative paths are resolved from the current folder; returns null for an invalid path or an existing file.
    private static string? ToFullPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path.Trim().Trim('"'), Directory.GetCurrentDirectory());
            return File.Exists(fullPath) ? null : fullPath;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    // A folder counts as empty when it doesn't exist or only holds repo files (.git, .gitignore, README, LICENSE).
    private static bool IsEmptyFolder(string path) =>
        !Directory.Exists(path)
        || Directory.EnumerateFileSystemEntries(path)
            .Select(Path.GetFileName)
            .All(entry => entry is not null
                && (entry.StartsWith('.')
                    || entry.StartsWith("README", StringComparison.OrdinalIgnoreCase)
                    || entry.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)));

    private static Templates.Choice PromptTemplate()
    {
        var template = AnsiConsole.Prompt(
            new SelectionPrompt<Templates.Choice>()
                .Title("Choisissez un [#8B9BFA]modèle[/]")
                .PageSize(8)
                .MoreChoicesText("[grey](Flèches haut et bas pour voir plus de choix)[/]")
                .HighlightStyle(new Style(Color.MediumPurple))
                .UseConverter(choice => choice.Name)
                .AddChoices(Templates.All));

        AnsiConsole.MarkupLine($"[green]✔[/] Modèle : [bold]{Markup.Escape(template.Name)}[/]");
        return template;
    }

    private static void ShowSummary(string name, Templates.Choice template, string outputDir)
    {
        var table = new Table().HideHeaders().AddColumns("Paramètre", "Valeur");
        table.AddRow("Nom", Markup.Escape(name));
        table.AddRow("Modèle", $"{Markup.Escape(template.Name)} [grey]({template.ShortName})[/]");
        table.AddRow("Dossier", Markup.Escape(outputDir));
        AnsiConsole.Write(table);
    }

    private static void ShowResult(string name, string outputDir)
    {
        var tree = new Tree($"[green]✔ {Markup.Escape(name)}[/] créé dans {Markup.Escape(outputDir)}");
        tree.AddNode($"{name}.sln");
        tree.AddNode("src").AddNode($"{name}.Api");
        var tests = tree.AddNode("tests");
        tests.AddNode($"{name}.Api.UnitTests");
        tests.AddNode($"{name}.Api.IntegrationTests");
        AnsiConsole.Write(tree);

        var run = $"dotnet run --project {Path.Combine("src", $"{name}.Api")}";
        var nextStep = outputDir == Directory.GetCurrentDirectory()
            ? run
            : $"cd \"{outputDir}\" puis {run}";
        AnsiConsole.MarkupLine($"Étape suivante : [#8B9BFA]{Markup.Escape(nextStep)}[/]");
    }

    private static readonly Regex ValidNamePattern =
        new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.Compiled);

    private static Regex ValidName() => ValidNamePattern;
}
