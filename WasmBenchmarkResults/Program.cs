using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace WasmBenchmarkResults
{
    public class Program
    {
        SortedDictionary<DateTimeOffset, ResultsData> timedPaths = new();

        static public int Main()
        {
            new Program().Run();

            return 0;
        }

        void Run()
        {
            FindResults(".");
            ExportCSV("results.csv");
        }

        void FindResults(string path)
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                if (!File.Exists(Path.Combine(dir, "git-log.txt")) || !File.Exists(Path.Combine(dir, "results.json")))
                    continue;

                var data = new ResultsData(dir);
                timedPaths[data.commitTime] = data;
            }
        }

        public void ExportCSV(string path)
        {
            using (var sw = new StreamWriter(path))
            {
                SortedSet<string> labels = new();
                foreach (var pair in timedPaths)
                {
                    Console.WriteLine($"date: {pair.Key} path: {pair.Value.runPath}");
                    labels.UnionWith(pair.Value.MeasurementLabels);
                }

                foreach (var l in labels)
                    Console.WriteLine($"l: {l}");

                sw.Write($"Task - Measurement");
                foreach (var d in timedPaths.Keys)
                {
                    sw.Write($",{d.Date.ToShortDateString()}");
                }

                sw.WriteLine();

                foreach (var l in labels)
                {
                    sw.Write($"{l.Replace(",", " -")}");

                    foreach (var p in timedPaths)
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
