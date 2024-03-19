using Mono.Options;

namespace Controller;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var controller = File.Exists("controller.json") ? Controller.Load() : new Controller();
        (var idleIds, var restartIds, var commitToSchedule) = ProcessArguments(args);

        await controller.Run(idleIds, restartIds, commitToSchedule);

        return 0;
    }

    static (List<int> idleIds, List<int> restartIds, List<string> commitsToSchedule) ProcessArguments(string[] args)
    {
        var help = false;
        var idleIds = new List<int>();
        var commitsToSchedule = new List<string>();
        var restartIds = new List<int>();

        var options = new OptionSet {
                $"Usage: Controller OPTIONS*",
                "",
                "The controller tool to schedule and run the wasm measurements on remote nodes.",
                "",
                "Options:",
                { "c|schedule-commit=",
                    "Schedule a commit to be run on idle node",
                    v => commitsToSchedule.Add(v) },
                { "i|idle=",
                    "Set node state to idle",
                    v => idleIds.Add(int.Parse(v)) },
                { "r|restart=",
                    "Restart measurement on node",
                    v => restartIds.Add(int.Parse(v)) },
                { "t|token=",
                    "GitHub API token",
                    v => GithubHelper.Token = v },
                { "h|help|?",
                    "Show this message and exit",
                    v => help = v != null },
            };

        options.Parse(args);

        if (help)
        {
            options.WriteOptionDescriptions(Console.Out);

            Environment.Exit(0);
        }

        return (idleIds, restartIds, commitsToSchedule);
    }
}
