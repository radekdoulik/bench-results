namespace WasmBenchmarkResults
{
    internal class Index
    {
        internal class Item
        {
            public string hash;
            public string flavor;
            public DateTimeOffset commitTime;
            public Dictionary<string, double> minTimes;
            public Dictionary<string, long> sizes;
        }

        static public List<Index.Item> Create(SortedDictionary<DateTimeOffset, ResultsData> timedPaths)
        {
            var list = new List<Index.Item>();

            foreach (var rd in timedPaths.Values)
                foreach (var fd in rd.results.Values)
                    list.Add(new Item()
                    {
                        hash = Path.GetFileName(rd.baseDirectory),
                        flavor = fd.flavor,
                        commitTime = fd.commitTime,
                        minTimes = fd.results.minTimes,
                        sizes = GetSizes(Path.Combine(rd.baseDirectory, fd.flavor.Replace('.', Path.DirectorySeparatorChar), "AppBundle"))
                    });

            return list;
        }

        static Dictionary<string, long> GetSizes(string path)
        {
            Console.WriteLine("get sizes of: " + path);
            if (!Directory.Exists(path))
                return null;

            var sizes = new Dictionary<string, long>();
            var ignoredFiles = new HashSet<string> { "results.html", "results.json" };
            sizes["AppBundle"] = GetDirectorySize(new DirectoryInfo(path), ignoredFiles);
            sizes["managed"] = GetDirectorySize(new DirectoryInfo(Path.Combine(path, "managed")));
            sizes["dotnet.wasm"] = new FileInfo(Path.Combine(path, "dotnet.wasm")).Length;
            sizes["icudt.dat"] = new FileInfo(Path.Combine(path, "icudt.dat")).Length;

            return sizes;
        }

        static long GetDirectorySize(DirectoryInfo di, HashSet<string> ignoredFiles = null)
        {
            long size = 0;
            size += di.EnumerateFiles().Sum(f => (ignoredFiles != null && ignoredFiles.Contains ($"{f.Name}.{f.Extension}")) ? 0 : f.Length);

            foreach(var si in di.EnumerateDirectories())
                size += GetDirectorySize(si);

            return size;
        }
    }
}
