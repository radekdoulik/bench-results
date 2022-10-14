﻿using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace WasmBenchmarkResults
{
    public class Program
    {
        SortedDictionary<DateTimeOffset, ResultsData> timedPaths = new();
        HashSet<string> flavors = new();

        static public int Main()
        {
            new Program().Run();

            return 0;
        }

        void Run()
        {
            FindResults("measurements");
            foreach (string flavor in flavors)
                ExportCSV($"csv/results.{flavor}.csv", flavor);

            GenerateReadme();
            GenerateIndex();
        }

        void GenerateIndex()
        {
            var indexData = Index.Create(timedPaths);
            var options = new JsonSerializerOptions { IncludeFields = true };
            var jsonData = JsonSerializer.Serialize<Index>(indexData, options);

            using var indexFileStream = new FileStream("measurements/index.zip", FileMode.Create);
            using var archive = new ZipArchive(indexFileStream, ZipArchiveMode.Create);
            var entry = archive.CreateEntry("index.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(jsonData);
        }

        readonly string[] Builds = { "aot", "interp" };
        readonly string[] Configs = { "default", "threads", "simd", "wasm-eh", "simd+wasm-eh" };
        readonly string[] Envs = { "chrome", "firefox", "v8", "node" };

        static bool ContainsResults(string dir) => File.Exists(Path.Combine(dir, "git-log.txt")) && File.Exists(Path.Combine(dir, "results.json"));

        void FindResults(string path)
        {
            foreach (var dir in Directory.GetDirectories(path))
                foreach (var build in Builds)
                    foreach (var config in Configs)
                        foreach (var env in Envs)
                        {
                            var flavor = $"{build}.{config}.{env}";
                            var flavoredDir = Path.Combine(dir, build, config, env);
                            if (!ContainsResults(flavoredDir))
                                continue;

                            flavors.Add(flavor);
                            FlavorData data = new FlavorData(flavoredDir, flavor);
                            ResultsData rd;
                            if (timedPaths.ContainsKey(data.commitTime))
                                rd = timedPaths[data.commitTime];
                            else
                            {
                                rd = new ResultsData { baseDirectory = dir };
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
            var intro = File.ReadAllText("README.md.in");
            using (var sw = new StreamWriter("README.md"))
            {
                sw.Write(intro);

                foreach (var res in timedPaths.Reverse())
                    sw.WriteLine(ReadmeLine(res));
            }
        }
    }
}
