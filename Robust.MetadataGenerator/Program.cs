using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;

namespace Robust.MetadataGenerator;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var service = new MetadataGeneratorService();

        var rootCommand = new RootCommand("Metadata generator for SS14 components and toolshed commands.");

        // --- "assemblies" subcommand ---
        var assembliesArg = new Argument<DirectoryInfo>("assemblies-dir")
        {
            Description = "Directory containing compiled .dll and .xml files"
        }.AcceptExistingOnly();
        var outputArg = new Argument<FileInfo>("output")
        {
            Description = "Output JSON file path"
        };
        var assembliesCommand = new Command("assemblies", "Scan compiled assemblies for metadata")
        {
            assembliesArg,
            outputArg,
        };
        assembliesCommand.SetAction(result =>
        {
            var assembliesDir = result.GetValue(assembliesArg);
            var output = result.GetValue(outputArg);
            var metadata = service.ProcessAssemblies(assembliesDir!.FullName).Result;
            service.WriteOutput(metadata, output!.FullName).Wait();
            return 0;
        });

        // --- "source" subcommand (explicit .csproj files) ---
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
        sourceCommand.SetAction((ParseResult result) =>
        {
            var output = result.GetValue(sourceOutputArg);
            var csprojFiles = result.GetValue(csprojOption);
            var paths = new List<string>();
            foreach (var f in csprojFiles ?? [])
            {
                paths.Add(f.FullName);
            }

            var metadata = service.ProcessSourceFiles(paths).Result;
            service.WriteOutput(metadata, output!.FullName).Wait();
            return 0;
        });

        // --- "source-discover" subcommand (discover .csproj from root dir) ---
        var discoverRootArg = new Argument<DirectoryInfo>("root-dir")
        {
            Description = "Root directory to search for .csproj files"
        };
        ArgumentValidation.AcceptExistingOnly(discoverRootArg);
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
        sourceDiscoverCommand.SetAction((ParseResult result) =>
        {
            var rootDir = result.GetValue(discoverRootArg);
            var output = result.GetValue(discoverOutputArg);
            var projectFilter = result.GetValue(filterOption) ?? "Content.*|RobustToolbox.*";
            var metadata = service.ProcessSourceDiscover(rootDir!.FullName, projectFilter).Result;
            service.WriteOutput(metadata, output!.FullName).Wait();
            return 0;
        });

        rootCommand.Add(assembliesCommand);
        rootCommand.Add(sourceCommand);
        rootCommand.Add(sourceDiscoverCommand);

        return await rootCommand.Parse(args).InvokeAsync();
    }
}

