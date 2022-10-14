﻿namespace WasmBenchmarkResults
{
    internal class Index
    {
        public IdMap FlavorMap = new();
        public IdMap MeasurementMap = new();
        public List<Item> Data = new();

        internal class Item
        {
            public string hash;
            public int flavorId;
            public DateTimeOffset commitTime;
            public Dictionary<int, double> minTimes;
            public Dictionary<int, long> sizes;
        }

        internal class IdMap : Dictionary<string, int>
        {
            readonly List<string> names = new();
            public string this[int id] => names[id];

            public new int this[string name]
            {
                get
                {
                    if (ContainsKey(name))
                        return base[name];

                    names.Add(name);
                    Add(name, names.Count - 1);

                    return names.Count - 1;
                }
            }
        }

        static public Index Create(SortedDictionary<DateTimeOffset, ResultsData> timedPaths)
        {
            var index = new Index();

            foreach (var rd in timedPaths.Values)
                foreach (var fd in rd.results.Values)
                {
                    var fId = index.FlavorMap[fd.flavor];
                    index.Data.Add(new Item()
                    {
                        hash = Path.GetFileName(rd.baseDirectory),
                        flavorId = fId,
                        commitTime = fd.commitTime,
                        minTimes = ConvertMinTimes(fd.results.minTimes, index.MeasurementMap),
                        sizes = GetSizes(Path.Combine(rd.baseDirectory, fd.flavor.Replace('.', Path.DirectorySeparatorChar), "AppBundle"), index.MeasurementMap)
                    });
                }

            return index;
        }

        static Dictionary<int, double> ConvertMinTimes(Dictionary<string, double> times, IdMap measurementsMap)
        {
            var ret = new Dictionary<int, double>();
            foreach (var pair in times)
                ret.Add(measurementsMap[pair.Key], pair.Value);

            return ret;
        }

        static Dictionary<int, long> GetSizes(string path, IdMap measurementsMap)
        {
            Console.WriteLine("get sizes of: " + path);
            if (!Directory.Exists(path))
                return null;

            var sizes = new Dictionary<int, long>();
            var ignoredFiles = new HashSet<string> { "results.html", "results.json" };
            sizes[measurementsMap["Size, AppBundle"]] = GetDirectorySize(new DirectoryInfo(path), ignoredFiles);
            sizes[measurementsMap["Size, managed"]] = GetDirectorySize(new DirectoryInfo(Path.Combine(path, "managed")));
            sizes[measurementsMap["Size, dotnet.wasm"]] = new FileInfo(Path.Combine(path, "dotnet.wasm")).Length;
            sizes[measurementsMap["Size, icudt.dat"]] = new FileInfo(Path.Combine(path, "icudt.dat")).Length;

            return sizes;
        }

        static long GetDirectorySize(DirectoryInfo di, HashSet<string> ignoredFiles = null)
        {
            long size = 0;
            size += di.EnumerateFiles().Sum(f => (ignoredFiles != null && ignoredFiles.Contains($"{f.Name}.{f.Extension}")) ? 0 : f.Length);

            foreach (var si in di.EnumerateDirectories())
                size += GetDirectorySize(si);

            return size;
        }
    }
}
