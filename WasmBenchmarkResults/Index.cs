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
                        minTimes = fd.results.minTimes
                    });

            return list;
        }
    }
}
