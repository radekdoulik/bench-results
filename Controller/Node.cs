using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Controller;

public class Node
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string IP { get; set; }
    public required string State { get; set; }
    public required string Commit { get; set; }
    public required string Command { get; set; }
    public required DateTime Time { get; set; }

    [JsonIgnore]
    public Controller? Controller { get; set; }

    const string logFile = "~/git/worker-bench.log";

    [JsonIgnore]
    public string Prefix => $"{ANSIColor.Color(Color.Green)}[{Id}]{ANSIColor.Reset}";

    [JsonIgnore]
    public string Status
    {
        get
        {
            StringBuilder sb = new($"{Prefix} {State}");
            if (State == "Running")
                sb.Append($" ({RoundedTimeWithColor}) {Commit[..7]}");
            else if (State == "Idle")
                sb.Append($" ({RoundedTimeWithColor})");

            return sb.ToString();
        }
    }

    public void Save()
    {
        Save($"node{Id}.json");
        WriteLine($"Saved {Name}");
        WriteLine($"  {this}");
    }

    public static Node Load(int id, Controller controller)
    {
        var node = Load($"node{id}.json", controller);

        return node;
    }

    override public string ToString()
    {
        return $"Id: {Id}, Name: {Name}, IP: {IP}, State: {State,8}, Commit: {Commit} Time: {Time,22} Command: {Command}";
    }

    public void Save(string filename)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string jsonString = JsonSerializer.Serialize(this, options);
        File.Move(filename, $"{filename}.bak", true);
        File.WriteAllText(filename, jsonString);
    }

    public static Node Load(string filename, Controller controller)
    {
        string jsonString = File.ReadAllText(filename);
        var node = JsonSerializer.Deserialize<Node>(jsonString) ?? throw new Exception($"Failed to load {filename}");
        node.Controller = controller;
        node.WriteLine($"Loaded {node}");

        return node;
    }

    public async Task Start(bool readLog = true)
    {
        WriteLine($"starting {Name}");
        if (State == "Running" && readLog)
            await ReadLog();
    }

    async Task<bool> UpdateScripts()
    {
        using var ssh = new SshHelper();

        WriteLine($"updating scripts on {Name}");

        var result = await ssh.Run($"{IP} cd ~/bench-results-tools; git pull -r 2>&1");

        WriteLine($"output: {ssh.output}");

        if (!result)
            WriteLine($"error: {ssh.errors}");

        return result;
    }

    public async Task ProcessCommit(string commit, bool allFlavors = false, bool threads = false)
    {
        Commit = commit;
        State = "Running";
        WriteLine($"starting bench of commit {Commit} on node {Name} allFlavors: {allFlavors}");

        await UpdateScripts();

        var flavors = allFlavors ? "" : "-d ";
        if (threads)
            flavors += "-t ";

        var args = $"{IP} nohup bash ~/bench-results-tools/scripts/bench-current.sh {flavors}-h {Commit} >> {logFile} 2>&1";
        Command = $"{SshHelper.Command} {args}";
        Time = DateTime.Now;
        Save();

        using (var ssh = new SshHelper())
        {
            await ssh.Run(args);
        }

        State = "Idle";
        WriteLine($"finished bench of commit {Commit}, bench run took {DateTime.Now - Time}");
        Time = DateTime.Now;
        Save();

        await LogTail(5);
    }

    void PrintLogTail(string str, int n = -1)
    {
        string[] lines = str.Split('\n');
        int startIndex = n >= 0 ? lines.Length - n : 0;
        if (startIndex < 0)
            startIndex = 0;

        for (int i = startIndex; i < lines.Length; i++)
        {
            WriteLine($"  {lines[i]}");
        }
    }

    public async Task LogTail(int lines = 8)
    {
        using var ssh = new SshHelper();
        await ssh.Run($"{IP} tail -n{lines} {logFile}");
        WriteLine($"log tail:");
        PrintLogTail(ssh.output.ToString());
    }

    public async Task ReadLog()
    {
        await LogTail();
        WriteLine($"{DateTime.Now - Time} elapsed, waiting for Done in the log");

        using var ssh = new SshHelper();
        ssh.outputFilter = (line) =>
        {
            if (line == "Done")
            {
                ssh.Stop();
            }
        };

        await ssh.Run($"{IP} tail -f {logFile}");

        WriteLine($"finished bench for {Commit}, bench run took {DateTime.Now - Time}");
        PrintLogTail(ssh.output.ToString(), 5);

        State = "Idle";
        Time = DateTime.Now;
        Save();

        Controller?.PrintStatus();
    }

    void WriteLine(string text)
    {
        Console.WriteLine($"{Prefix} {text}");
    }

    string RoundedTimeWithColor => $"{ANSIColor.Color(Color.Yellow)}{(DateTime.Now - Time).RoundToNearestSeconds()}{ANSIColor.Reset}";
}
