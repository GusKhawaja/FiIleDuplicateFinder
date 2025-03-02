﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SciFiDuplicateFinder.Models
{
    public class FileScanner
    {
        public async Task<List<DuplicateFileInfo>> FindDuplicatesAsync(
            string rootPath,
            IProgress<string> currentFileProgress,
            IProgress<int> progressValue,
            CancellationToken token)
        {
            var duplicates = new List<DuplicateFileInfo>();
            var filesBySize = new Dictionary<long, List<string>>();

            // For logging errors
            string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DuplicateFinderLog.txt");

            // 1) Gather all files
            var allFiles = GetAllFiles(rootPath, logFilePath, token);
            if (token.IsCancellationRequested)
                return duplicates;

            int totalFiles = allFiles.Count;

            // 2) Group by file size
            for (int i = 0; i < totalFiles; i++)
            {
                token.ThrowIfCancellationRequested();

                string filePath = allFiles[i];
                try
                {
                    long size = new FileInfo(filePath).Length;
                    if (!filesBySize.ContainsKey(size))
                    {
                        filesBySize[size] = new List<string>();
                    }
                    filesBySize[size].Add(filePath);
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logFilePath, $"Error accessing file size for {filePath}: {ex.Message}\r\n");
                }

                // Update progress (0-50%)
                double step = ((double)(i + 1) / totalFiles) * 50;
                progressValue.Report((int)step);

                currentFileProgress.Report(filePath);
            }

            // 3) Hash within each size group in parallel
            int groupIndex = 0;
            int totalGroups = filesBySize.Count;

            foreach (var kvp in filesBySize)
            {
                token.ThrowIfCancellationRequested();

                var sameSizeFiles = kvp.Value;
                if (sameSizeFiles.Count > 1)
                {
                    var hashToFilePaths = new ConcurrentDictionary<string, List<string>>();

                    Parallel.ForEach(sameSizeFiles, new ParallelOptions
                    {
                        CancellationToken = token,
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    },
                    (file) =>
                    {
                        token.ThrowIfCancellationRequested();
                        currentFileProgress.Report(file);

                        string hash = ComputeFileHash(file, logFilePath);
                        if (hash == null) return;

                        hashToFilePaths.AddOrUpdate(hash,
                            new List<string> { file },
                            (h, existing) =>
                            {
                                existing.Add(file);
                                return existing;
                            });
                    });

                    // Check for duplicates in each hash group
                    foreach (var groupByHash in hashToFilePaths)
                    {
                        if (groupByHash.Value.Count > 1)
                        {
                            // Further group by file name
                            var fileNameGroups = new Dictionary<string, List<string>>();
                            foreach (string path in groupByHash.Value)
                            {
                                string name = Path.GetFileName(path);
                                if (!fileNameGroups.ContainsKey(name))
                                    fileNameGroups[name] = new List<string>();
                                fileNameGroups[name].Add(path);
                            }

                            // Only add duplicates if same name + same hash
                            foreach (var groupByName in fileNameGroups)
                            {
                                if (groupByName.Value.Count > 1)
                                {
                                    foreach (string dupPath in groupByName.Value)
                                    {
                                        var fi = new FileInfo(dupPath);
                                        duplicates.Add(new DuplicateFileInfo
                                        {
                                            FolderPath = fi.DirectoryName,
                                            FileName = fi.Name,
                                            FileSize = fi.Length
                                        });
                                    }
                                }
                            }
                        }
                    }
                }

                groupIndex++;
                double groupProgress = 50 + ((double)groupIndex / totalGroups) * 50;
                progressValue.Report((int)groupProgress);
            }

            return duplicates;
        }

        private List<string> GetAllFiles(string rootPath, string logFilePath, CancellationToken token)
        {
            var files = new List<string>();
            var dirs = new Stack<string>();
            dirs.Push(rootPath);

            while (dirs.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string currentDir = dirs.Pop();

                try
                {
                    foreach (string subDir in Directory.GetDirectories(currentDir))
                    {
                        dirs.Push(subDir);
                    }
                    foreach (string file in Directory.GetFiles(currentDir))
                    {
                        files.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logFilePath, $"Error accessing {currentDir}: {ex.Message}\r\n");
                }
            }
            return files;
        }

        private string ComputeFileHash(string filePath, string logFilePath)
        {
            try
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hashBytes = md5.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", "");
            }
            catch (Exception ex)
            {
                File.AppendAllText(logFilePath, $"Error hashing file {filePath}: {ex.Message}\r\n");
                return null;
            }
        }
    }
}
