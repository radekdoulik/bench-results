using System.Text.Json;
using System.Text.Json.Serialization;

namespace Controller;

public class Controller
{
    readonly List<Node> nodes = [];
    public string lastProcessedCommit = "";
    public string lastQueriedCommit = "";
    public DateTimeOffset lastProcessTime = DateTimeOffset.MinValue;
    public int runs = 0;

    [JsonIgnore]
    readonly List<Task> tasks = [];

    [JsonIgnore]
    string LastProcessedCommit
    {
        get => lastProcessedCommit;
        set
        {
            lastProcessedCommit = value;
            lastProcessTime = DateTimeOffset.Now;
            Save();
        }
    }

    static async Task Sleep(int minutes)
    {
        await Task.Delay(minutes * 60000);
    }

    public async Task Run(List<int> idleIds, List<int> restartIds, List<string> commitsToSchedule)
    {
        PrintTimeFromLastCommit();
        SetupNodes();

        List<int> restartedIds = [];
        foreach (var id in restartIds)
        {
            if (RestartNode(id))
                restartedIds.Add(id);
        }

        foreach (var id in idleIds)
        {
            var node = nodes[id - 1];
            WriteLine($"{ANSIColor.Color(Color.LightBlue)}Setting node {ANSIColor.Color(Color.Yellow)}{node.Name}{ANSIColor.Color(Color.LightBlue)} to idle{ANSIColor.Reset}");
            node.State = "Idle";
            node.Save();
        }

        foreach (var node in nodes)
        {
            tasks.Add(node.Start(!restartedIds.Contains(node.Id)));
        }

        ScheduleCommits();

        string commitToProcess = "";
        var newCommitTask = WaitForNewCommit(0);
        tasks.Add(newCommitTask);

        while (true)
        {
            //Console.WriteLine($"Waiting for {tasks.Count} tasks");

            var idx = Task.WaitAny(tasks.ToArray());
            WriteLine($"Task {idx} finished. Status: {tasks[idx].Status}");

            if (tasks[idx].Exception != null)
            {
                WriteLine($"Task {idx} failed: {tasks[idx].Exception}");
            }

            if (tasks[idx] == newCommitTask)
            {
                if (tasks[idx].Status == TaskStatus.RanToCompletion)
                {
                    if (lastProcessTime > DateTimeOffset.Now.AddMinutes(-50))
                    {
                        WriteLine($"The commit is too recent, waiting (time from the last processed commit: {DateTimeOffset.Now - lastProcessTime} {LastProcessedCommit})");
                    }
                    else
                    {
                        commitToProcess = newCommitTask.Result;
                    }
                }

                newCommitTask = WaitForNewCommit(5);
                tasks.Add(newCommitTask);
            }

            tasks.RemoveAt(idx);
            ScheduleCommits();

            if (string.IsNullOrEmpty(commitToProcess))
            {
                if (nodes.Count(n => n.State == "Idle") > 1)
                {
                    var idleIdx = nodes.FindIndex(n => n.State == "Idle");
                    var gap = await FillGap();
                    if (gap != null)
                    {
                        WriteLine($"{ANSIColor.Color(Color.LightBlue)}Run measurement of {gap.Hash} to fill the gap on node {nodes[idleIdx].Id}{ANSIColor.Reset}");
                        runs++;
                        Save();
                        tasks.Add(nodes[idleIdx].ProcessCommit(gap.Hash));
                    }
                }

                PrintStatus();
                continue;
            }

            foreach (var node in nodes)
            {
                if (node.State == "Idle")
                {
                    runs++;
                    PrintTimeFromLastCommit();
                    LastProcessedCommit = commitToProcess;
                    tasks.Add(node.ProcessCommit(commitToProcess, runs % 5 == 0, runs % 5 == 2));
                    commitToProcess = "";
                    break;
                }
            }

            PrintStatus();
        }

        bool RestartNode(int id)
        {
            var idx = id - 1;
            if (id > nodes.Count || nodes[idx].Id != id)
            {
                WriteLine($"{ANSIColor.Color(Color.Red)}Node {id} does not exist, cannot restart it{ANSIColor.Reset}");
                return false;
            }

            if (nodes[idx].State == "Idle")
            {
                WriteLine($"{ANSIColor.Color(Color.Red)}Node {id} is already idle, cannot restart it{ANSIColor.Reset}");
                return false;
            }

            if (string.IsNullOrEmpty(nodes[idx].Commit))
            {
                WriteLine($"{ANSIColor.Color(Color.Red)}Node {id} does not have a commit to restart, cannot restart it{ANSIColor.Reset}");
                return false;
            }

            WriteLine($"{ANSIColor.Color(Color.LightBlue)}Restarting node {id}{ANSIColor.Reset}");
            tasks.Add(nodes[idx].ProcessCommit(nodes[idx].Commit, false));

            return true;
        }

        void ScheduleCommits()
        {
            foreach (var node in nodes)
            {
                if (node.State == "Idle")
                {
                    if (commitsToSchedule.Count < 1)
                        return;

                    runs++;
                    Save();
                    tasks.Add(node.ProcessCommit(commitsToSchedule[0], false));
                    commitsToSchedule.RemoveAt(0);
                }
            }
        }
    }

    void AddNode(int id)
    {
        nodes.Add(new Node { Id = id, Name = $"Node {id}", IP = $"192.168.2.{172 + id}", State = "Idle", Commit = "", Command = "", Controller = this, Time = DateTime.Now });
    }

    internal void SetupNodes()
    {
        if (!RestoreNodes())
            for (int i = 1; i <= 3; i++)
                AddNode(i);

        nodes.Sort((a, b) => a.Id - b.Id);
    }

    internal async Task<string> WaitForNewCommit(int minutes = 5)
    {
        await Sleep(minutes);

        while (true)
        {
            string commit;
            try
            {
                commit = await GithubHelper.GetLatestCommitHash("dotnet/runtime");
            }
            catch (System.Text.Json.JsonException e)
            {
                WriteLine($"{ANSIColor.Color(Color.Red)}Error: {e.Message}{ANSIColor.Reset}");
                await Sleep(5);

                continue;
            }

            if (commit != lastQueriedCommit)
            {
                WriteLine($"Latest commit in the repo: {commit}");
                lastQueriedCommit = commit;
            }

            if (commit != LastProcessedCommit)
            {
                WriteLine($"The new commit that was not processed yet: {commit}");
                return commit;
            }

            PrintStatus();

            await Sleep(5);
        }
    }


    internal bool RestoreNodes()
    {
        foreach (var file in Directory.GetFiles(".", "node*.json"))
        {
            var node = Node.Load(file, this);
            nodes.Add(node);
        }

        Console.WriteLine($"Restored {nodes.Count} nodes");

        return nodes.Count > 0;
    }

    void Save(string filename = "controller.json")
    {
        var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        string jsonString = JsonSerializer.Serialize(this, options);
        File.Move(filename, $"{filename}.bak", true);
        File.WriteAllText(filename, jsonString);
    }

    public static Controller Load()
    {
        string jsonString = File.ReadAllText("controller.json");
        var options = new JsonSerializerOptions { IncludeFields = true };
        var controller = JsonSerializer.Deserialize<Controller>(jsonString, options) ?? throw new Exception($"Failed to load controller.json");
        WriteLine($"Loaded state: {ANSIColor.Color(Color.Yellow)}{controller}{ANSIColor.Reset}");

        return controller;
    }

    async Task<CommitInfo?> FillGap()
    {
        var commits = await GithubHelper.GetCommitsInLast14Days("dotnet/runtime");
        var slice = await GithubHelper.GetIndexDataFromUrl("https://github.com/radekdoulik/WasmPerformanceMeasurements/raw/main/measurements/slices/last.zip");

        if (slice == null)
        {
            Console.WriteLine($"{ANSIColor.Color(Color.Red)}Failed to get the slice{ANSIColor.Reset}");

            return null;
        }

        var measuredCommits = slice.Index.Data.Select(d => new CommitInfo(d.hash, DateTimeOffset.Parse(d.commitTime))).DistinctBy(c => c.Hash).ToList();
        var running = nodes.Where(n => n.State == "Running").Select(n => n.Commit).ToList();
        foreach (var commit in running)
        {
            var idx = commits.FindIndex(c => c.Hash == commit);
            if (idx != -1)
                measuredCommits.Add(commits[idx]);
        }

        commits.Sort((a, b) => b.Date.CompareTo(a.Date));
        measuredCommits.Sort((a, b) => b.Date.CompareTo(a.Date));

        // System.Console.WriteLine($"commits {commits.Count}");
        // foreach(var c in commits)
        // {
        //     Console.WriteLine($"{c}");
        // }

        // System.Console.WriteLine($"measured commits {measuredCommits.Count}");
        // foreach(var c in measuredCommits)
        // {
        //     Console.WriteLine($"{c}");
        // }

        var commitsHashes = commits.Select(c => c.Hash).ToList();
        //var measuredCommitsHashes = measuredCommits.Select(c => c.Hash).ToList();

        var gaps = new List<(CommitInfo Commit, int Gap)>();
        var idx1 = commitsHashes.IndexOf(measuredCommits[0].Hash);
        for (int i = 0; i < measuredCommits.Count - 1; i++)
        {
            var idx2 = commitsHashes.IndexOf(measuredCommits[i + 1].Hash);
            int gap = idx2 - idx1;
            //System.Console.WriteLine($"{measuredCommits[i]} gap: {gap} {idx1} {idx2} end: {measuredCommits[i + 1]}");

            if (idx1 != -1 && idx2 != -1)
            {
                // System.Console.WriteLine($"add gap, {measuredCommits[i]} gap: {gap} {idx1} {idx2} end: {measuredCommits[i + 1]}");
                gaps.Add((measuredCommits[i], gap));
            }

            idx1 = idx2;
        }

        gaps.Sort((a, b) => b.Gap - a.Gap);

        if (gaps.Count > 0 && gaps[0].Gap > 1)
        {
            var idx = commitsHashes.IndexOf(gaps[0].Commit.Hash) + gaps[0].Gap / 2;
            CommitInfo commitInMiddle = idx >= commits.Count
                ? commits[^1]
                : commits[idx];

            WriteLine($"Commit in the middle of the largest gap: {gaps[0].Gap}");
            WriteLine($"  commit: {gaps[0].Commit}");
            WriteLine($"  middle: {commitInMiddle}");

            return commitInMiddle;
        }

        return null;
    }

    public override string ToString()
    {
        return $"lastProcessedCommit: {LastProcessedCommit}, lastProcessTime: {lastProcessTime}, lastQueriedCommit: {lastQueriedCommit} runs: {runs}";
    }

    public void PrintStatus()
    {
        Write($"{ANSIColor.Color(Color.Yellow)}runs: {runs} tasks: {tasks.Count} nodes: {nodes.Count}{ANSIColor.Reset}");
        foreach (var node in nodes)
        {
            Console.Write($" {node.Status}");
        }
        Console.WriteLine(ANSIColor.Reset);
    }

    public void PrintTimeFromLastCommit()
    {
        WriteLine($"Time from last benchmark start: {ANSIColor.Color(Color.Yellow)}{DateTimeOffset.Now - lastProcessTime}{ANSIColor.Reset}");
    }

    static void Write(string text)
    {
        Console.Write($"{ANSIColor.Color(Color.Green)}[c]{ANSIColor.Reset} {text}");
    }

    static void WriteLine(string text)
    {
        Write($"{text}");
        Console.WriteLine(ANSIColor.Reset);
    }
}
