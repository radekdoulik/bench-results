using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Controller;

public partial class GithubHelper
{
    private static readonly HttpClient client = new();

    public static readonly string DateFormat = "yyyy-MM-ddTHH:mm:ssZ";

    public static async Task<string> GetLatestCommitHash(string repository)
    {
        var commits = await GetCommits(repository);
        if (commits == null || commits.Count == 0)
            throw new Exception($"No commits found in {repository}");

        return commits[0].Hash;
    }

    public static async Task<List<CommitInfo>> GetCommitsInLast14Days(string repository)
    {
        var since = DateTime.UtcNow.AddDays(-14).ToString(DateFormat);
        var until = DateTime.UtcNow.ToString(DateFormat);
        var commits = await GetCommits(repository, since, until, true);

        if (commits == null)
            return [];

        return commits;
    }



    private static async Task<List<CommitInfo>?> GetCommits(string repository, string? since = null, string? until = null, bool allPages = false)
    {
        var primaryUrl = $"https://api.github.com/repos/{repository}/commits";
        var args = "";
        if (!(string.IsNullOrEmpty(since) || string.IsNullOrEmpty(until)))
        {
            args += $"since={since}&until={until}";
        }

        var url = primaryUrl;
        if (!string.IsNullOrEmpty(args))
            url = $"{primaryUrl}?{args}";

        System.Console.WriteLine(url);

        var commits = await GetCommitsFromUrl(url);
        if (!allPages || commits == null)
        {
            return commits;
        }

        var page = 2;
        var prefix = string.IsNullOrEmpty(args) ? "?" : "&";
        do
        {
            var urlWithPage = $"{url}{prefix}page={page}";
            var pageOfCommits = await GetCommitsFromUrl(urlWithPage);
            if (pageOfCommits == null || pageOfCommits.Count == 0)
                break;

            commits.AddRange(pageOfCommits);
            page++;
        } while (true);

        return commits;
    }

    private static async Task<List<CommitInfo>> GetCommitsFromUrl(string url)
    {
        client.DefaultRequestHeaders.Add("User-Agent", "request");
        var response = await client.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();
        var commits = JsonSerializer.Deserialize<List<Commit>?>(json) ?? throw new Exception($"Failed to parse commits from {url}");
        if (commits == null)
            throw new Exception($"Failed to get commits from {url}");

        return commits.Select(c => new CommitInfo(c.sha, c.commit.committer.date)).ToList();
    }

    public static async Task<byte[]> GetContentFromUrl(string url)
    {
        using (var client = new HttpClient())
        {
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }
    }

    public static async Task<Slice?> GetIndexDataFromUrl(string url)
    {
        var indexJson = await ExtractFileFromZip(await GetContentFromUrl(url), "index.json");

        return JsonSerializer.Deserialize<Slice>(indexJson);
    }

    private static async Task<string> ExtractFileFromZip(byte[] zipBytes, string fileName)
    {
        using var memoryStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(memoryStream);
        var entry = archive.GetEntry(fileName) ?? throw new Exception($"File '{fileName}' not found in the zip archive.");
        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);

        return await reader.ReadToEndAsync();
    }
}
