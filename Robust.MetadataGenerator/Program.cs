using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Robust.MetadataGenerator.Helpers;

namespace Robust.MetadataGenerator;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var logger = GetLoggerFactory(LogLevel.Debug);
        var service = new MetadataGeneratorService(logger);

        var rootCommand = new RootCommand("Metadata generator for SS14 components and toolshed commands.")
        {
            CsprojMode(service),
            DiscoveryMode(service, logger)
        };

        return await rootCommand.Parse(args)
            .InvokeAsync();
    }

    // --- "source-discover" subcommand (discover .csproj from root dir) ---
    private static Command DiscoveryMode(MetadataGeneratorService service, ILogger logger)
    {
        var discoverRootArg = new Argument<DirectoryInfo>("root-dir")
        {
            Description = "Root directory to search for .csproj files"
        };
        discoverRootArg.AcceptExistingOnly();
        var discoverOutputArg = new Argument<FileInfo>("output")
        {
            Description = "Output JSON file path"
        };
        var filterOption = new Option<string>("--project-filter", ["-f"])
        {
            Description = "Pipe-separated filter prefixes for project file names",
        };
        var sourceDiscoverCommand = new Command("source-discover", "Discover .csproj files under a directory and scan them")
        {
            discoverRootArg,
            discoverOutputArg,
            filterOption,
        };
        sourceDiscoverCommand.SetAction(async (result,ct) =>
        {
            var rootDir = result.GetValue(discoverRootArg);
            var output = result.GetValue(discoverOutputArg);
            var projectFilter = result.GetValue(filterOption) ?? "Content.*|RobustToolbox.*";
            var projectFiles = ProjectFilterHelpers.DiscoverSources(rootDir!.FullName, projectFilter, logger);
            var metadata = await service.ProcessSourceFiles(projectFiles, ct);
            await service.WriteOutput(metadata, output!.FullName);
        });
        return sourceDiscoverCommand;
    }

    // --- "source" subcommand (explicit .csproj files) ---
    private static Command CsprojMode(MetadataGeneratorService service)
    {
        var sourceOutputArg = new Argument<FileInfo>("output")
        {
            Description = "Output JSON file path"
        };
        var csprojOption = new Option<FileInfo[]>("--csproj", ["-p"])
        {
            Description = "One or more .csproj files to scan",
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.OneOrMore,
        };
        var sourceCommand = new Command("source", "Scan source projects by specifying .csproj files")
        {
            sourceOutputArg,
            csprojOption,
        };
        sourceCommand.SetAction(async (result, ct) =>
        {
            var output = result.GetValue(sourceOutputArg);
            var csprojFiles = result.GetValue(csprojOption);
            var paths = (csprojFiles ?? []).Select(x => x.FullName)
                .ToArray();

            var metadata = await service.ProcessSourceFiles(paths, ct);
            await service.WriteOutput(metadata, output!.FullName);
        });
        return sourceCommand;
    }

    private static ILogger<MetadataGeneratorService> GetLoggerFactory(LogLevel minLevel = LogLevel.Information)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.IncludeScopes = false;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(minLevel);
        });

        return loggerFactory.CreateLogger<MetadataGeneratorService>();
    }
}

