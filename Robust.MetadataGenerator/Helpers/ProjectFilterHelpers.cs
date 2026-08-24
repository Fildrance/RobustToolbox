using Microsoft.Extensions.FileSystemGlobbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Robust.MetadataGenerator.Helpers;

public static class ProjectFilterHelpers
{
    /// <summary>
    /// Discover .csproj files under a root directory matching filter patterns.
    /// </summary>
    /// <param name="rootDir">Directory to be scanned for csproj files. Scan will be recursive.</param>
    /// <param name="projectFilter">Filter to be applied to search results.</param>
    /// <param name="logger">Logger for outputting intermediate results.</param>
    /// <returns>List of paths for found project files.</returns>
    public static IReadOnlyList<string> DiscoverSources(string rootDir, string projectFilter, ILogger logger)
    {
        if (!Directory.Exists(rootDir))
        {
            logger.LogError("Root directory not found: {RootDir}", rootDir);
            return [];
        }

        logger.LogInformation("Scanning projects under: {RootDir}\r\n Project filter: {ProjectFilter}", rootDir, projectFilter);

        var filterPatterns = projectFilter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var matcher = new Matcher();
        matcher.AddInclude("**.csproj");
        matcher.AddExclude("**/obj/**");
        matcher.AddExclude("**/bin/**");
        foreach (var filterPattern in filterPatterns)
        {
            matcher.AddInclude(filterPattern);
        }

        var matches = matcher.Match(Directory.GetFiles(rootDir, "*.csproj", SearchOption.AllDirectories));
        var csprojFiles = matches.Files.Select(x => x.Path)
            .Distinct()
            .ToArray();

        logger.LogInformation("Found {Count} matching project files", csprojFiles.Length);
        return csprojFiles;
    }
}
