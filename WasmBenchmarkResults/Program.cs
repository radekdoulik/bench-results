using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Mono.Options;

namespace WasmBenchmarkResults
{
    public class Program
    {
        SortedDictionary<DateTimeOffset, ResultsData> timedPaths = new();
        HashSet<string> flavors = new();
        static string? AddPath = null;
        static bool AddCSV = false;
        static string IndexPath = "measurements/index.zip";
        readonly string IndexJsonFilename = "index.json";
        public static bool Verbose = false;

        static public int Main(string[] args)
        {
            ProcessArguments(args);

            new Program().Run();

            return 0;
        }

        void Run()
        {
            if (AddPath != null)
            {
                var index = LoadIndex();
                FindResults(AddPath);

                if (Verbose)
                    Console.WriteLine($"  measurement tasks: {index.MeasurementMap.Count}\n  flavors: {index.FlavorMap.Count}\n  measurements: {index.Data.Count}");

                index.AddResults(timedPaths, true);
                SaveIndex(index);

                return;
            }

            FindAllResults("measurements");

            if (AddCSV)
            {
                foreach (string flavor in flavors)
                {
                    if (!Directory.Exists("csv"))
                        Directory.CreateDirectory("csv");

                    ExportCSV($"csv/results.{flavor}.csv", flavor);
                }
            }

            GenerateReadme();
            GenerateIndex();
        }

        internal class LatestData
        {
            public DateTimeOffset FirstDate;
            public DateTimeOffset SliceStartDate;
            public DateTimeOffset SliceEndDate;
            public Index Index;
        }

        void SaveIndex(Index index, string path)
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var json = JsonSerializer.Serialize<Index>(index, options);
            SaveJsonInZip(json, path);
        }

        void SaveJsonInZip(string json, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var fileStream = new FileStream(path, FileMode.Create);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
            var entry = archive.CreateEntry(IndexJsonFilename);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(json);
        }

        void SaveIndex(Index index)
        {
            index.Sort();
            SaveIndex(index, IndexPath);
            SaveSlicedIndex(index);
        }

        void SaveSlicedIndex(Index index)
        {
            var latest = index.GetLatest14Days();
            var options = new JsonSerializerOptions { IncludeFields = true };
            var json = JsonSerializer.Serialize<LatestData>(new LatestData { FirstDate = index.Data[0].commitTime, SliceStartDate = latest.Data[0].commitTime, SliceEndDate = latest.Data[latest.Data.Count -1].commitTime, Index = latest }, options);

            SaveJsonInZip(json, "measurements/slices/last.zip");
        }

        void GenerateIndex()
        {
            SaveIndex(Index.Create(timedPaths));
        }

        Index LoadIndex()
        {
            if (Verbose)
                Console.WriteLine($"Loading index: {IndexPath}");

            using var indexFileStream = new FileStream(IndexPath, FileMode.Open);
            using var archive = new ZipArchive(indexFileStream, ZipArchiveMode.Read);
            using var stream = archive.GetEntry(IndexJsonFilename).Open();
            var options = new JsonSerializerOptions { IncludeFields = true };

            return JsonSerializer.Deserialize<Index>(stream, options);
        }

        readonly string[] Builds = { "aot", "interp" };
        readonly string[] Configs = { "default", "threads", "simd", "wasm-eh", "simd+wasm-eh", "legacy", "hybrid-globalization", "nosimd" };
        readonly string[] Envs = { "chrome", "firefox", "v8", "node" };

        static bool ContainsResults(string dir) => File.Exists(Path.Combine(dir, "git-log.txt")) && File.Exists(Path.Combine(dir, "results.json"));

        void FindAllResults(string path)
        {
            foreach (var dir in Directory.GetDirectories(path))
                FindResults(dir);
        }

        void FindResults(string path)
        {
            foreach (var build in Builds)
                foreach (var config in Configs)
                    foreach (var env in Envs)
                    {
                        var flavor = $"{build}.{config}.{env}";
                        var flavoredDir = Path.Combine(path, build, config, env);
                        if (!ContainsResults(flavoredDir))
                            continue;

                        flavors.Add(flavor);
                        FlavorData data = new FlavorData(flavoredDir, flavor);
                        ResultsData rd;
                        if (timedPaths.ContainsKey(data.commitTime))
                            rd = timedPaths[data.commitTime];
                        else
                        {
                            rd = new ResultsData { baseDirectory = path };
                            timedPaths[data.commitTime] = rd;
                        }

                        rd.results[flavor] = data;
                    }
        }

        public void ExportCSV(string path, string flavor = "aot.default.chrome")
        {
            using (var sw = new StreamWriter(path))
            {
                SortedDictionary<DateTimeOffset, FlavorData> flavoredData = new();
                SortedSet<string> labels = new();
                foreach (var pair in timedPaths)
                {
                    if (!pair.Value.results.ContainsKey(flavor))
                        continue;

                    var fd = pair.Value.results[flavor];
                    flavoredData[fd.commitTime] = fd;
                    Console.WriteLine($"date: {fd.commitTime} path: {fd.runPath}");
                    labels.UnionWith(fd.MeasurementLabels);
                }

                foreach (var l in labels)
                    Console.WriteLine($"l: {l}");

                sw.Write($"Task - Measurement");
                foreach (var d in flavoredData.Keys)
                {
                    sw.Write($",{d.Date.ToShortDateString()}");
                }

                sw.WriteLine();

                foreach (var l in labels)
                {
                    sw.Write($"{l.Replace(",", " -")}");

                    foreach (var p in flavoredData)
                    {
                        var mt = p.Value.results.minTimes;
                        var v = mt.ContainsKey(l) ? mt[l].ToString() : "N/A";
                        sw.Write($",{v}");
                    }

                    sw.WriteLine();
                }

                sw.Close();
            }
        }

        string ReadmeLine(KeyValuePair<DateTimeOffset, ResultsData> pair)
        {
            StringBuilder sb = new();

            var dir = Path.GetFileName(pair.Value.baseDirectory);
            sb.Append($"{pair.Key.ToString("d")} - [{dir.Substring(0, 7)}](https://github.com/dotnet/runtime/commit/{dir})");
            foreach (var fd in pair.Value.results)
                sb.Append($" :: [{fd.Key}]({fd.Value.runPath.Replace(@".\", "").Replace(@"\", "/")})");

            sb.AppendLine();

            return sb.ToString();
        }


        void GenerateReadme()
        {
            var readme = "README.md.in";
            if (!File.Exists(readme))
            {
                if (Verbose)
                    Console.WriteLine($"{readme} file not found, it will not be updated");

                return;
            }

            var intro = File.ReadAllText(readme);
            using (var sw = new StreamWriter("README.md"))
            {
                sw.Write(intro);

                foreach (var res in timedPaths.Reverse())
                    sw.WriteLine(ReadmeLine(res));
            }
        }

        static List<string> ProcessArguments(string[] args)
        {
            var help = false;
            var options = new OptionSet {
                $"Usage: WasmBenchmarkResults OPTIONS*",
                "",
                "Creates or updated the result files",
                "",
                "Copyright 2023 Microsoft Corporation",
                "",
                "Options:",
                { "a|add-measurements=",
                    "Add measurements under the {PATH}",
                    v => AddPath = v },
                { "c|add-csv-files",
                    "Add CSV files with measurements",
                    v => AddCSV = true },
                { "i|index-path=",
                    "Specify index {PATH}, measurements/index.zip is the default value",
                    v => IndexPath = v },
                { "h|help|?",
                    "Show this message and exit",
                    v => help = v != null },
                { "v",
                    "Be verbose",
                    v => Verbose = true },
            };

            var remaining = options.Parse(args);

            if (help || args.Length < 1)
            {
                options.WriteOptionDescriptions(Console.Out);

                Environment.Exit(0);
            }

            return remaining;
        }
    }
}
