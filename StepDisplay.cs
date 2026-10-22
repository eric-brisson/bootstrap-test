using Spectre.Console;
using Spectre.Console.Rendering;

namespace Starter.Init;

// Columns of the step list: status icon, colored label and duration.
// Spectre has no "failed" state (a stopped task counts as finished), so failures are tracked here.
internal sealed class StepDisplay
{
    private static readonly Color SpinnerColor = Color.FromHex("#A6E22E");

    private readonly HashSet<int> failedTaskIds = [];

    public ProgressColumn[] Columns =>
    [
        new StatusColumn(this),
        new TaskDescriptionColumn { Alignment = Justify.Left },
        new DurationColumn(),
    ];

    public void MarkFailed(ProgressTask task) => failedTaskIds.Add(task.Id);

    private bool IsFailed(ProgressTask task) => failedTaskIds.Contains(task.Id);

    // ○ pending, green spinner running, ✔ done, ✘ failed.
    private sealed class StatusColumn(StepDisplay display) : ProgressColumn
    {
        private readonly SpinnerColumn spinner = new(Spinner.Known.Dots)
        {
            Style = new Style(SpinnerColor),
            PendingText = "[grey]○[/]",
            CompletedText = "[green]✔[/]",
        };

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime) =>
            display.IsFailed(task) ? new Markup("[red]✘[/]") : spinner.Render(options, task, deltaTime);

        public override int? GetColumnWidth(RenderOptions options) => spinner.GetColumnWidth(options);
    }

    // Time spent on the step, ticking while it runs.
    private sealed class DurationColumn : ProgressColumn
    {
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime) =>
            task.ElapsedTime is { } elapsed
                ? new Markup($"[grey]{elapsed.TotalSeconds:0.0} s[/]")
                : Text.Empty;
    }
}
