using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

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

        public void Sort()
        {
            Data.Sort((x, y) => x.commitTime.CompareTo(y.commitTime));
        }

        public Index GetLatest14Days()
        {
            var index = new Index();
            index.FlavorMap = FlavorMap;
            index.MeasurementMap = MeasurementMap;

            var edge = Data[Data.Count - 1].commitTime.AddDays(-14);
            index.Data = Data.FindAll(i => i.commitTime >= edge);
            index.Sort();

            return index;
        }

        public void AddResults(SortedDictionary<DateTimeOffset, ResultsData> timedPaths, bool update = false)
        {
            foreach (var rd in timedPaths.Values)
                foreach (var fd in rd.results.Values)
                {
                    var appBundleDir = Path.Combine(rd.baseDirectory, fd.flavor.Replace('.', Path.DirectorySeparatorChar), "AppBundle");
                    if (!Directory.Exists(appBundleDir))
                        appBundleDir = Path.Combine(rd.baseDirectory, fd.flavor.Replace('.', Path.DirectorySeparatorChar), "..", "AppBundle");
                    var fId = FlavorMap[fd.flavor];
                    var newItem = new Item()
                    {
                        hash = Path.GetFileName(rd.baseDirectory),
                        flavorId = fId,
                        commitTime = fd.commitTime,
                        minTimes = ConvertMinTimes(fd.results.minTimes, MeasurementMap),
                        sizes = GetSizes(appBundleDir, MeasurementMap)
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
                        {
                            Data.Add(newItem);
                            if (Program.Verbose)
                                Console.WriteLine($"Added {fd.flavor} {fd.commitTime}");
                        }
                        else
                        {
                            Data.Insert(iIdx, newItem);
                            if (Program.Verbose)
                                Console.WriteLine($"Updated {fd.flavor} {fd.commitTime}");
                        }
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

        static void TryAddSizeOfDirectory(string path, string name, Dictionary<int, long> sizes, IdMap measurementsMap, string basePath = "")
        {
            var combined = Path.Combine(path, name);

            if (Directory.Exists(combined))
            {
                var prefix = string.IsNullOrEmpty(basePath) ? string.Empty : $"{basePath}/";
                var key = $"Size, {prefix}{name}";
                sizes[measurementsMap[key]] = GetDirectorySize(new DirectoryInfo(combined));
                if (Program.Verbose)
                    Console.WriteLine($"  dir path: {path} name: {name} size: {sizes[measurementsMap[key]]} key: {key}");
            }
        }

        static readonly Regex hashRegex = new(@"^[a-z0-9]{10}$", RegexOptions.Compiled);

        static string GetNameForKey(string name)
        {
            static int GetHashDotIndex(string name)
            {
                var lastDotIndex = name.LastIndexOf('.');
                var rv = HasHashBeforeIndex(name, lastDotIndex);
                if (rv < 0 && lastDotIndex > 0)
                {
                    var noExt = name.Substring(0, lastDotIndex);
                    lastDotIndex = noExt.LastIndexOf('.');

                    return HasHashBeforeIndex(noExt, lastDotIndex);
                }

                return rv;
            }

            static int HasHashBeforeIndex(string name, int lastDotIndex)
            {
                if (lastDotIndex <= 0 || lastDotIndex < 11)
                    return -1;

                var hashStartIndex = lastDotIndex - 10;
                if (name[hashStartIndex - 1] != '.')
                    return -1;

                var hashPart = name.Substring(hashStartIndex, 10);
                return hashRegex.IsMatch(hashPart) ? lastDotIndex : -1;
            }

            var lastDotIndex = GetHashDotIndex(name);
            if (lastDotIndex > 0)
            {
                var hashLength = 10;
                var hashStartIndex = lastDotIndex - hashLength;
                if (hashStartIndex > 0 && name[hashStartIndex - 1] == '.')
                {
                    var hashPart = name.Substring(hashStartIndex, hashLength);
                    if (hashRegex.IsMatch(hashPart))
                    {
                        return name.Remove(hashStartIndex - 1, hashLength + 1);
                    }
                }
            }

            return name;
        }

        static void TryAddSizeOfFile(string path, string name, Dictionary<int, long> sizes, IdMap measurementsMap, string basePath = "")
        {
            var combined = Path.Combine(path, GetNameForKey(name));
            if (File.Exists(combined))
            {
                var key = $"Size, {basePath}/{name}";
                sizes[measurementsMap[key]] = new FileInfo(combined).Length;
                if (Program.Verbose)
                    Console.WriteLine($"  file path: {path} name: {name} size: {sizes[measurementsMap[key]]} key: {key}");
            }
        }

        static Dictionary<int, long> GetSizes(string path, IdMap measurementsMap)
        {
            if (Program.Verbose)
                Console.WriteLine("Get sizes of: " + path);

            if (!Directory.Exists(path))
                return null;

            var sizes = new Dictionary<int, long>();
            var ignoredFiles = new HashSet<string> { "results.html", "results.json" };
            var ignoredDirectories = new HashSet<string> { "browser-template", "blazor-template" };
            var size = sizes[measurementsMap["Size, AppBundle"]] = GetDirectorySize(new DirectoryInfo(path), ignoredFiles, ignoredDirectories);
            if (Program.Verbose)
                Console.WriteLine($"  size of AppBundle: {size}");

            TryAddSizeOfDirectory(path, "managed", sizes, measurementsMap);
            TryAddSizeOfDirectory(path, "_framework", sizes, measurementsMap);
            TryAddSizeOfFile(path, "dotnet.wasm", sizes, measurementsMap);
            TryAddSizeOfFile(path, "dotnet.native.wasm", sizes, measurementsMap);
            TryAddSizeOfFile(path, "icudt.dat", sizes, measurementsMap);
            TryAddSizeOfFile(path, "icudt_no_CJK.dat", sizes, measurementsMap);

            AddSizeOfDirectory(path, "blazor-template", sizes, measurementsMap);
            AddSizeOfDirectory(path, "browser-template", sizes, measurementsMap);

            return sizes;
        }

        static void AddSizeOfDirectory(string path, string name, Dictionary<int, long> sizes, IdMap measurementsMap, string basePath = "")
        {
            if (!Directory.Exists(path))
                return;

            var combined = Path.Combine(path, basePath);
            var dir = Path.Combine(combined, name);
            if (!Directory.Exists(combined) || !Directory.Exists(dir))
                return;

            var di = new DirectoryInfo(dir);
            TryAddSizeOfDirectory(combined, name, sizes, measurementsMap, basePath);

            foreach (var fi in di.EnumerateFiles())
            {
                TryAddSizeOfFile(dir, fi.Name, sizes, measurementsMap, Path.Combine(basePath, di.Name));
            }

            foreach (var cdi in di.EnumerateDirectories())
            {
                AddSizeOfDirectory(path, cdi.Name, sizes, measurementsMap, Path.Combine(basePath, name));
            }
        }

        static long GetDirectorySize(DirectoryInfo di, HashSet<string>? ignoredFiles = null, HashSet<string>? ignoredDirectories = null)
        {
            long size = 0;
            size += di.EnumerateFiles().Sum(f => (ignoredFiles != null && ignoredFiles.Contains($"{f.Name}.{f.Extension}")) ? 0 : f.Length);

            foreach (var si in di.EnumerateDirectories())
            {
                if (ignoredDirectories != null && ignoredDirectories.Contains(si.Name))
                    continue;

                size += GetDirectorySize(si);
            }

            return size;
        }

        (IdMap, Dictionary<int,int>) FixMeasurementMap()
        {
            var newMeasurementMap = new IdMap();
            var newOldMap = new Dictionary<int, int>();
            foreach (var key in MeasurementMap.Keys)
            {
                var newKey = GetNameForKey(key);
                newOldMap[MeasurementMap[key]] = newMeasurementMap[newKey];
            }

            return (newMeasurementMap, newOldMap);
        }

        Dictionary<int, long> FixSizeKeys(Dictionary<int, long> sizes, IdMap newMeasurementMap, Dictionary<int, int> newOldMap)
        {
            var newSizes = new Dictionary<int, long>();
            foreach (var pair in sizes)
            {
                if (newOldMap.TryGetValue(pair.Key, out var newKey))
                    newSizes[newKey] = pair.Value;
                else
                    System.Console.WriteLine($"Warning: size key {pair.Key} not found in new map, keeping old value {pair.Value}");
            }

            return newSizes;
        }

        internal void FixSizes()
        {
            (var newMeasurementMap, var newOldMap) = FixMeasurementMap();
            if (Program.Verbose)
                System.Console.WriteLine($"Fixing sizes, old map: {MeasurementMap.Count} new map: {newMeasurementMap.Count}");

            foreach (var item in Data)
            {
                if (item.sizes != null)
                    item.sizes = FixSizeKeys(item.sizes, newMeasurementMap, newOldMap);
            }

            MeasurementMap = newMeasurementMap;
        }
    }
}
