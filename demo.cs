using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Starter.Init;

internal static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Windows consoles default to a legacy code page without symbols like ◆ and ✔.
        Console.OutputEncoding = Encoding.UTF8;

        ShowHeader();

        // A name passed as argument skips every prompt (for scripts) and uses the first template.
        var interactive = args.Length == 0;
        var name = interactive ? PromptName() : args[0];
        var template = interactive ? PromptTemplate() : Templates.All[0];

        if (!ValidName().IsMatch(name))
        {
            AnsiConsole.MarkupLine($"[red]'{Markup.Escape(name)}' n'est pas un espace de noms C# valide, par exemple Sample.Orders[/]");
            return 1;
        }

        var outputDir = Directory.GetCurrentDirectory();
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

        ShowSummary(name, template, outputDir);
        if (interactive && !await ConfirmAsync("Générer le projet ?"))
        {
            return 0;
        }

        var solution = Path.Combine(outputDir, $"{name}.sln");
        Step[] steps =
        [
            new("Génération du projet", "Projet généré", ["new", template.ShortName, "--name", name, "--output", outputDir]),
            new("Restauration des paquets", "Paquets restaurés", ["restore", solution]),
            new("Compilation de la solution", "Solution compilée", ["build", solution, "--no-restore"]),
        ];

        if (!await RunStepsAsync(steps))
        {
            return 1;
        }

        ShowResult(name, outputDir);
        return 0;
    }

    private sealed record Step(string Running, string Done, string[] Arguments);

    // Runs each step under a spinner and prints a line as each one completes.
    private static Task<bool> RunStepsAsync(IReadOnlyList<Step> steps) =>
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(new Style(Color.MediumPurple))
            .StartAsync(steps[0].Running, async context =>
            {
                foreach (var step in steps)
                {
                    context.Status($"{step.Running}...");
                    var stopwatch = Stopwatch.StartNew();

                    var result = await DotNetCli.RunAsync(step.Arguments);
                    if (result.ExitCode != 0)
                    {
                        AnsiConsole.MarkupLine($"[red]✘[/] {step.Running} [red]a échoué[/]");
                        AnsiConsole.WriteLine(result.Output);
                        return false;
                    }

                    AnsiConsole.MarkupLine($"[green]✔[/] {step.Done} [grey]({stopwatch.Elapsed.TotalSeconds:0.0} s)[/]");
                }

                return true;
            });

    private static void ShowHeader()
    {
        var version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0];

        AnsiConsole.MarkupLine("[#8B9BFA]◆ STARTER INIT[/]");
        AnsiConsole.MarkupLine($"[grey]Outil de création de projets · v{version}[/]");
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        AnsiConsole.MarkupLine("[bold]Créer un nouveau projet[/]");
        AnsiConsole.MarkupLine("[grey]Choisissez un nom et un modèle pour commencer.[/]");
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

        AnsiConsole.MarkupLine("Étape suivante : [#8B9BFA]dotnet test[/]");
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex ValidName();
}
