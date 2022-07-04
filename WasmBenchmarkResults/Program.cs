using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

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
            FindResults(".");
            foreach (string flavor in flavors)
                ExportCSV($"results.{flavor}.csv", flavor);
        }

        string[] Builds = { "aot", "interp" };
        string[] Configs = { "default", "threads", "simd" };
        string[] Envs = { "chrome", "firefox", "v8", "node" };

        bool ContainsResults(string dir) => File.Exists(Path.Combine(dir, "git-log.txt")) && File.Exists(Path.Combine(dir, "results.json"));

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
                                rd = new ResultsData();
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
    }
}
