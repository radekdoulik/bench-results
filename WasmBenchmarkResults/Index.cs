namespace WasmBenchmarkResults
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
            public new int this[string name]
            {
                get
                {
                    if (ContainsKey(name))
                        return base[name];

                    var idx = Keys.Count;
                    Add(name, idx);

                    return idx;
                }
            }
        }

        static public Index Create(SortedDictionary<DateTimeOffset, ResultsData> timedPaths)
        {
            var index = new Index();
            index.AddResults(timedPaths);

            return index;
        }

        public void AddResults(SortedDictionary<DateTimeOffset, ResultsData> timedPaths, bool update = false)
        {
            foreach (var rd in timedPaths.Values)
                foreach (var fd in rd.results.Values)
                {
                    var fId = FlavorMap[fd.flavor];
                    var newItem = new Item()
                    {
                        hash = Path.GetFileName(rd.baseDirectory),
                        flavorId = fId,
                        commitTime = fd.commitTime,
                        minTimes = ConvertMinTimes(fd.results.minTimes, MeasurementMap),
                        sizes = GetSizes(Path.Combine(rd.baseDirectory, fd.flavor.Replace('.', Path.DirectorySeparatorChar), "AppBundle"), MeasurementMap)
                    };

                    if (update)
                    {
                        int iIdx = 0;
                        foreach (var d in Data)
                        {
                            if (d.commitTime > newItem.commitTime)
                                break;

                            iIdx++;
                        }

			if (iIdx >= Data.Count)
			    Data.Add(newItem);
			else
			    Data.Insert(iIdx, newItem);
                    }
                    else
                        Data.Add(newItem);
                }
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
            if (Program.Verbose)
                Console.WriteLine("Get sizes of: " + path);

            if (!Directory.Exists(path))
                return null;

            var sizes = new Dictionary<int, long>();
            var ignoredFiles = new HashSet<string> { "results.html", "results.json" };
            sizes[measurementsMap["Size, AppBundle"]] = GetDirectorySize(new DirectoryInfo(path), ignoredFiles);
            sizes[measurementsMap["Size, managed"]] = GetDirectorySize(new DirectoryInfo(Path.Combine(path, "managed")));
            if (File.Exists(Path.Combine(path, "dotnet.wasm")))
                sizes[measurementsMap["Size, dotnet.wasm"]] = new FileInfo(Path.Combine(path, "dotnet.wasm")).Length;
            if (File.Exists(Path.Combine(path, "icudt.dat")))
                sizes[measurementsMap["Size, icudt.dat"]] = new FileInfo(Path.Combine(path, "icudt.dat")).Length;
            if (File.Exists(Path.Combine(path, "icudt_no_CJK.dat")))
                sizes[measurementsMap["Size, icudt_no_CJK.dat"]] = new FileInfo(Path.Combine(path, "icudt_no_CJK.dat")).Length;

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
