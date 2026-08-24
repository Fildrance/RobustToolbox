using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Robust.MetadataGenerator.Helpers;
using Robust.MetadataGenerator.Models;

namespace Robust.MetadataGenerator;

public sealed class MetadataGeneratorService(ILogger<MetadataGeneratorService> logger)
{
    /// <summary>
    /// Scan specific .csproj files with Roslyn.
    /// </summary>
    public async Task<MetadataCollection> ProcessSourceFiles(IReadOnlyList<string> csprojFiles, CancellationToken ct)
    {
        if (csprojFiles.Count == 0)
        {
            logger.LogError("No project files to process.");
            return new MetadataCollection();
        }

        // Use a single workspace so all project references resolve properly
        var workspace = MSBuildWorkspace.Create();
        using var registration = workspace.RegisterWorkspaceFailedHandler(args =>
            {
                if(args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure
                    && !args.Diagnostic.Message.Contains("Consider removing this package from your dependencies, as it is likely unnecessary"))
                {
                    logger.LogError("Workspace processing failed! Error: {}", args.Diagnostic.Message);
                }
            });

        await FillWorkspace(csprojFiles, workspace, ct);

        // Compile all projects first to get all compilations
        var compilations = await PrepareCompilationData(workspace, ct);

        // Resolve base types for metadata
        IReadOnlyList<string> baseTypeNames =
        [
            "Robust.Shared.GameObjects.Component",
            "Robust.Shared.Toolshed.ToolshedCommand",
            "Robust.Shared.Console.IConsoleCommand"
        ];
        var baseTypes = ResolveBaseTypes(compilations, baseTypeNames);

        return ExtractMetadata(compilations, baseTypes);
    }

    /// <summary>
    /// Serialize metadata collection to JSON and write to file.
    /// </summary>
    public async Task WriteOutput(MetadataCollection metadata, string outputPath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        var json = JsonSerializer.Serialize(metadata, options);
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        await File.WriteAllTextAsync(outputPath, json);
        logger.LogInformation("Metadata written to: {OutputPath}", outputPath);
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var countByType = metadata.Metadata.GroupBy(x => x.Kind)
                .ToDictionary(x => x.Key, x => x.Count());
            foreach (var (kind, count) in countByType)
            {
                logger.LogDebug("Found {count} classes of kind {kind}", count, kind);
            }
        }
    }

    private MetadataCollection ExtractMetadata(List<Compilation> compilations, IReadOnlyCollection<INamedTypeSymbol> baseTypes)
    {
        // merge types across compilations (e.g., Content.Server compilation may be empty
        // when Content.Shared is also loaded). So we process every compilation and
        // deduplicate by full type name.
        var metadata = new MetadataCollection();
        var seenFullNames = new HashSet<string>();

        foreach (var type in GetEligibleTypes(compilations))
        {
            var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!seenFullNames.Add(fullName))
                continue;

            foreach (var baseType in baseTypes)
            {
                if (!type.MatchesBaseType(baseType))
                    continue;

                logger.LogDebug("Found: {TypeName} ({Kind})", type.Name, baseType.Name);

                ExtractAndAddType(type, baseType.Name, metadata, seenFullNames);
                break;
            }
        }

        CollectReferencedTypes(compilations, metadata, seenFullNames);
        return metadata;
    }

    /// <summary>
    /// Returns all non-abstract, non-generic, non-auto-generated class types across all compilations.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> GetEligibleTypes(List<Compilation> compilations)
    {
        return FilterTypes(compilations.SelectMany(x => x.GlobalNamespace.GetAllTypes()));
    }

    private static IEnumerable<INamedTypeSymbol> FilterTypes(IEnumerable<INamedTypeSymbol> types)
    {
        return types.Where(type => type is { TypeKind: TypeKind.Class, IsAbstract: false, IsGenericType: false }
                                   && !type.HasAttribute("RobustAutoGeneratedAttribute")
                                   && !type.Name.Contains('_')
        );
    }

    /// <summary>
    /// Extract type metadata and its nested types, adding them to the collection.
    /// </summary>
    private static void ExtractAndAddType(
        INamedTypeSymbol type,
        string kind,
        MetadataCollection metadata,
        HashSet<string> seenFullNames)
    {
        var typeInfo = type.ExtractTypeMetadata(kind);
        metadata.Metadata.Add(typeInfo);

        foreach (var nested in FilterTypes(type.GetTypeMembers()))
        {
            var nestedName = nested.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (seenFullNames.Add(nestedName))
            {
                var nestedInfo = nested.ExtractTypeMetadata("nested");
                metadata.Metadata.Add(nestedInfo);
            }
        }
    }

    private void CollectReferencedTypes(
        List<Compilation> compilations,
        MetadataCollection metadata,
        HashSet<string> seenFullNames
    )
    {
        // Build a lookup of full name -> type symbol across all compilations
        var typeLookup = GetEligibleTypes(compilations)
            .GroupBy(x=>x.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .ToDictionary(x => x.Key, x => x.First());

        // Recursively collect referenced types until no new types are discovered.
        // This ensures that if type A references type B, and type B references type C,
        // type C is also added to the metadata.
        var queue = new Queue<string>();

        // Seed the queue with field types from already-collected metadata entries
        foreach (var field in metadata.Metadata.SelectMany(entry => entry.Fields))
        {
            if (TryCollectTypeReference(field.Type, typeLookup, seenFullNames, out var fullName))
                queue.Enqueue(fullName);
        }

        // Process recursively: when a referenced type is added, also scan its fields
        while (queue.TryDequeue(out var currentName))
        {
            if (!typeLookup.TryGetValue(currentName, out var typeSymbol))
                continue;

            // Skip types from System.* assemblies
            var assemblyName = typeSymbol.ContainingAssembly?.Name ?? "";
            if (assemblyName.StartsWith("System.") || assemblyName == "System")
            {
                seenFullNames.Add(currentName);
                continue;
            }

            logger.LogDebug("Found referenced type: {TypeName}", typeSymbol.Name);

            ExtractAndAddType(typeSymbol, "referenced", metadata, seenFullNames);

            // Scan fields of this newly added type for further references
            var typeInfo = typeSymbol.ExtractTypeMetadata("referenced");
            foreach (var field in typeInfo.Fields)
            {
                if (TryCollectTypeReference(field.Type, typeLookup, seenFullNames, out var nestedRef))
                    queue.Enqueue(nestedRef);
            }
        }
    }

    private static bool TryCollectTypeReference(
        string typeFullName,
        Dictionary<string, INamedTypeSymbol> typeLookup,
        HashSet<string> seenFullNames,
        [NotNullWhen(true)]out string? fullName)
    {
        fullName = null;

        if (seenFullNames.Contains(typeFullName))
            return false;

        if (!typeLookup.TryGetValue(typeFullName, out var typeSymbol))
            return false;

        // Skip types from System.* namespaces or assemblies
        var cleanName = typeFullName;
        if (cleanName.StartsWith("global::"))
            cleanName = cleanName[8..];

        var assemblyName = typeSymbol.ContainingAssembly?.Name ?? "";
        if (cleanName.StartsWith("System.") || cleanName == "System"
            || assemblyName.StartsWith("System.") || assemblyName == "System")
            return false;

        fullName = typeFullName;
        return true;
    }

    private IReadOnlyCollection<INamedTypeSymbol> ResolveBaseTypes(List<Compilation> compilations, IReadOnlyList<string> baseTypeNames)
    {
        Dictionary<string, INamedTypeSymbol> baseTypes = new();
        foreach (var compilation in compilations)
        {
            foreach (var baseTypeName in baseTypeNames)
            {
                var foundType = compilation.GetTypeByMetadataName(baseTypeName);
                if(foundType != null)
                    baseTypes[baseTypeName] = foundType;
            }
        }

        foreach (var baseTypeName in baseTypeNames)
        {
            if (!baseTypes.ContainsKey(baseTypeName))
            {
                logger.LogWarning("Could not resolve {typeName} base type from any project", baseTypeName);
            }
        }

        return baseTypes.Values;
    }

    private async Task<List<Compilation>> PrepareCompilationData(MSBuildWorkspace workspace, CancellationToken ct)
    {
        var compilations = new List<Compilation>();
        foreach (var project in workspace.CurrentSolution.Projects)
        {
            logger.LogInformation("Compiling: {ProjectName}", project.Name);
            try
            {
                var compilation = await project.GetCompilationAsync(ct);
                if (compilation != null)
                    compilations.Add(compilation);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not compile {ProjectName}: {Message}", project.Name, ex.Message);
            }
        }

        return compilations;
    }

    private async Task FillWorkspace(IReadOnlyList<string> csprojFiles, MSBuildWorkspace workspace, CancellationToken ct)
    {
        foreach (var csprojPath in csprojFiles)
        {
            if (!File.Exists(csprojPath))
            {
                logger.LogError("Project not found: {CsprojPath}", csprojPath);
                continue;
            }

            var fullPath = Path.GetFullPath(csprojPath);

            // Check if this project is already in the workspace (loaded as a dependency)
            var alreadyLoaded = workspace.CurrentSolution.Projects
                .Any(p => p.FilePath != null && Path.GetFullPath(p.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase));

            if (alreadyLoaded)
            {
                logger.LogInformation("Skipping (already in workspace): {FileName}", Path.GetFileName(csprojPath));
                continue;
            }

            logger.LogInformation("Loading: {FileName}", Path.GetFileName(csprojPath));
            try
            {
                await workspace.OpenProjectAsync(csprojPath, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed: {Message}", ex.Message);
            }
        }
    }

}
