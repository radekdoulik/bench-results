using System.Text.Json;

namespace Controller;

public class Commit
{
    public required string sha { get; set; }
}

public class GithubHelper
{
    private static readonly HttpClient client = new();

    public static async Task<string> GetLatestCommitHash(string repository)
    {
        client.DefaultRequestHeaders.Add("User-Agent", "request");

        var response = await client.GetAsync($"https://api.github.com/repos/{repository}/commits");
        var json = await response.Content.ReadAsStringAsync();
        var commits = JsonSerializer.Deserialize<List<Commit>>(json) ?? throw new Exception($"Failed to parse commits from {repository}");

        return commits[0].sha;
    }
}
