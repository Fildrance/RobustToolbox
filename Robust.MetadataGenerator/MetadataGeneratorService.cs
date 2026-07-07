using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.FileSystemGlobbing;
using Robust.MetadataGenerator.Models;

namespace Robust.MetadataGenerator;

public sealed class MetadataGeneratorService
{
    /// <summary>
    /// Discover .csproj files under a root directory matching filter patterns, then scan them with Roslyn.
    /// </summary>
    public async Task<MetadataCollection> ProcessSourceDiscover(string rootDir, string projectFilter)
    {
        if (!Directory.Exists(rootDir))
        {
            Console.Error.WriteLine($"Root directory not found: {rootDir}");
            return new MetadataCollection();
        }

        Console.WriteLine($"Scanning projects under: {rootDir}");
        Console.WriteLine($"Project filter: {projectFilter}");

        var filterPatterns = projectFilter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var csprojFiles = Directory.GetFiles(rootDir, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("\\node_modules\\"))
            .Where(f => filterPatterns.Any(p => Path.GetFileName(f).StartsWith(p.Replace("*", ""), StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToArray();

        Console.WriteLine($"Found {csprojFiles.Length} matching project files");
        return await ProcessSourceFiles(csprojFiles);
    }

    /// <summary>
    /// Scan specific .csproj files with Roslyn.
    /// </summary>
    public async Task<MetadataCollection> ProcessSourceFiles(IReadOnlyList<string> csprojFiles)
    {
        if (csprojFiles.Count == 0)
        {
            Console.Error.WriteLine("No project files to process.");
            return new MetadataCollection();
        }

        // Use a single workspace so all project references resolve properly
        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, _) => { };

        foreach (var csprojPath in csprojFiles)
        {
            if (!File.Exists(csprojPath))
            {
                Console.Error.WriteLine($"  Project not found: {csprojPath}");
                continue;
            }

            var fullPath = Path.GetFullPath(csprojPath);

            // Check if this project is already in the workspace (loaded as a dependency)
            var alreadyLoaded = workspace.CurrentSolution.Projects
                .Any(p => p.FilePath != null && Path.GetFullPath(p.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase));

            if (alreadyLoaded)
            {
                Console.WriteLine($"  Skipping (already in workspace): {Path.GetFileName(csprojPath)}");
                continue;
            }

            Console.WriteLine($"  Loading: {Path.GetFileName(csprojPath)}");
            try
            {
                await workspace.OpenProjectAsync(csprojPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"    Failed: {ex.Message}");
            }
        }

        // Compile all projects first to get all compilations
        var compilations = new List<(Project Project, Compilation Compilation)>();
        foreach (var project in workspace.CurrentSolution.Projects)
        {
            Console.WriteLine($"  Compiling: {project.Name}");
            try
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation != null)
                    compilations.Add((project, compilation));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"    Could not compile {project.Name}: {ex.Message}");
            }
        }

        // Resolve base types
        INamedTypeSymbol? componentBaseType = null;
        INamedTypeSymbol? toolshedCommandBaseType = null;
        foreach (var (_, compilation) in compilations)
        {
            componentBaseType ??= compilation.GetTypeByMetadataName("Robust.Shared.GameObjects.Component");
            toolshedCommandBaseType ??= compilation.GetTypeByMetadataName("Robust.Shared.Toolshed.ToolshedCommand");
        }

        if (componentBaseType == null)
            Console.Error.WriteLine("Warning: Could not resolve Component base type from any project");
        if (toolshedCommandBaseType == null)
            Console.Error.WriteLine("Warning: Could not resolve ToolshedCommand base type from any project");

        // Process all project compilations and collect components/commands with dedup.
        // When multiple projects are loaded in the same workspace, MSBuildWorkspace may
        // merge types across compilations (e.g., Content.Server compilation may be empty
        // when Content.Shared is also loaded). So we process every compilation and
        // deduplicate by full type name.
        var metadata = new MetadataCollection();
        var seenFullNames = new HashSet<string>();

        foreach (var (project, compilation) in compilations)
        {
            foreach (var type in GetAllTypes(compilation.GlobalNamespace))
            {
                if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsGenericType)
                    continue;

                var isComponent = componentBaseType != null && IsDerivedFrom(type, componentBaseType);
                var isCommand = toolshedCommandBaseType != null && IsDerivedFrom(type, toolshedCommandBaseType);

                if (!isComponent && !isCommand)
                    continue;

                var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!seenFullNames.Add(fullName))
                    continue;

                Console.WriteLine($"  Found: {type.Name} ({(isComponent ? "Component" : "Command")})");

                var typeInfo = ExtractTypeMetadata(type);
                typeInfo.Kind = isComponent ? "Component" : "Command";

                if (isComponent)
                    metadata.Components.Add(typeInfo);
                else
                    metadata.Commands.Add(typeInfo);
            }
        }

        return metadata;
    }

    /// <summary>
    /// Scan compiled assemblies for metadata (reflection mode).
    /// </summary>
    public Task<MetadataCollection> ProcessAssemblies(string assembliesDir)
    {
        if (!Directory.Exists(assembliesDir))
        {
            Console.Error.WriteLine($"Assemblies directory not found: {assembliesDir}");
            return Task.FromResult(new MetadataCollection());
        }

        Console.WriteLine($"Processing assemblies from: {assembliesDir}");

        var xmlDocCache = new Dictionary<string, System.Xml.Linq.XDocument>();
        foreach (var xmlFile in Directory.GetFiles(assembliesDir, "*.xml"))
        {
            try
            {
                xmlDocCache[Path.GetFileNameWithoutExtension(xmlFile)] = System.Xml.Linq.XDocument.Load(xmlFile);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Could not load XML doc {xmlFile}: {ex.Message}");
            }
        }

        var metadata = new MetadataCollection();
        foreach (var dllPath in Directory.GetFiles(assembliesDir, "*.dll"))
        {
            try
            {
                var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
                var assemblyName = assembly.GetName().Name ?? "";
                xmlDocCache.TryGetValue(assemblyName, out var xmlDoc);

                foreach (var type in assembly.GetExportedTypes())
                {
                    if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                        continue;

                    bool isComponent = false, isCommand = false;
                    var baseType = type.BaseType;
                    while (baseType != null)
                    {
                        if (baseType.FullName == "Robust.Shared.GameObjects.Component") isComponent = true;
                        if (baseType.FullName == "Robust.Shared.Toolshed.ToolshedCommand") isCommand = true;
                        baseType = baseType.BaseType;
                    }
                    if (!isComponent && !isCommand) continue;

                    Console.WriteLine($"  Found: {type.Name} ({(isComponent ? "Component" : "Command")})");
                    var typeInfo = ExtractTypeMetadataReflection(type, xmlDoc);
                    typeInfo.Kind = isComponent ? "Component" : "Command";
                    if (isComponent) metadata.Components.Add(typeInfo);
                    else metadata.Commands.Add(typeInfo);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Could not load assembly {dllPath}: {ex.Message}");
            }
        }
        return Task.FromResult(metadata);
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
        Console.WriteLine($"\nMetadata written to: {outputPath}");
        Console.WriteLine($"  Components: {metadata.Components.Count}");
        Console.WriteLine($"  Commands: {metadata.Commands.Count}");
    }

    // ==================== Roslyn internals ====================

    private static TypeMetadata ExtractTypeMetadata(INamedTypeSymbol typeSymbol)
    {
        var typeInfo = new TypeMetadata
        {
            FullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Name = typeSymbol.Name,
            Namespace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "",
            AssemblyName = typeSymbol.ContainingAssembly?.Name ?? "",
            Summary = GetDocumentationSummary(typeSymbol),
            SeeAlso = GetDocumentationSeeAlso(typeSymbol),
        };

        ExtractAccessReferences(typeSymbol, typeInfo);

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IFieldSymbol field && field.DeclaredAccessibility == Accessibility.Public && !field.IsConst && !field.IsReadOnly)
            {
                typeInfo.Fields.Add(new FieldMetadata
                {
                    Name = field.Name,
                    Type = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Summary = GetDocumentationSummary(field),
                    SeeAlso = GetDocumentationSeeAlso(field),
                });
            }
            else if (member is IPropertySymbol prop && prop.DeclaredAccessibility == Accessibility.Public && !prop.IsReadOnly && !prop.IsWriteOnly)
            {
                typeInfo.Properties.Add(new PropertyMetadata
                {
                    Name = prop.Name,
                    Type = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Summary = GetDocumentationSummary(prop),
                    SeeAlso = GetDocumentationSeeAlso(prop),
                });
            }
        }

        foreach (var nestedType in typeSymbol.GetTypeMembers())
        {
            var nestedInfo = ExtractTypeMetadata(nestedType);
            if (!string.IsNullOrEmpty(nestedInfo.Summary) || nestedInfo.Fields.Count > 0 || nestedInfo.Properties.Count > 0)
                typeInfo.NestedTypes.Add(nestedInfo);
        }

        ExtractRelatedSystems(typeSymbol, typeInfo);
        return typeInfo;
    }

    private static string? GetDocumentationSummary(ISymbol symbol)
    {
        var xmlDoc = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xmlDoc))
            return null;

        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xmlDoc);
            var summary = doc.Root?.Element("summary")?.Value;
            if (summary != null)
            {
                summary = System.Xml.XmlConvert.DecodeName(summary);
                summary = summary.Trim();
                return string.IsNullOrEmpty(summary) ? null : summary;
            }
        }
        catch { }
        return null;
    }

    private static List<string>? GetDocumentationSeeAlso(ISymbol symbol)
    {
        var xmlDoc = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xmlDoc))
            return null;

        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xmlDoc);
            var refs = new List<string>();
            foreach (var seeEl in doc.Descendants("see").Concat(doc.Descendants("seealso")))
            {
                var cref = seeEl.Attribute("cref")?.Value;
                if (!string.IsNullOrEmpty(cref))
                    refs.Add(cref);
                var href = seeEl.Attribute("href")?.Value;
                if (!string.IsNullOrEmpty(href) && !refs.Contains(href))
                    refs.Add(href);
            }
            return refs.Count > 0 ? refs : null;
        }
        catch { }
        return null;
    }

    private static void ExtractAccessReferences(INamedTypeSymbol typeSymbol, TypeMetadata typeInfo)
    {
        var accessAttr = typeSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name is "AccessAttribute" or "Access");
        if (accessAttr == null) return;

        var accessRefs = new List<AccessReference>();
        foreach (var arg in accessAttr.ConstructorArguments)
        {
            if (arg.Kind == TypedConstantKind.Type)
            {
                var typeName = arg.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!string.IsNullOrEmpty(typeName))
                    accessRefs.Add(new AccessReference { TargetType = typeName, Access = "ReadWrite" });
            }
            else if (arg.Kind == TypedConstantKind.Array && arg.Values != null)
            {
                foreach (var element in arg.Values)
                {
                    if (element.Kind == TypedConstantKind.Type)
                    {
                        var typeName = element.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (!string.IsNullOrEmpty(typeName))
                            accessRefs.Add(new AccessReference { TargetType = typeName, Access = "ReadWrite" });
                    }
                }
            }
        }
        foreach (var namedArg in accessAttr.NamedArguments)
        {
            var accessKind = namedArg.Key switch
            {
                "ReadType" => "Read",
                "WriteType" => "Write",
                _ => null
            };
            if (namedArg.Value.Kind == TypedConstantKind.Type)
            {
                var typeName = namedArg.Value.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!string.IsNullOrEmpty(typeName))
                    accessRefs.Add(new AccessReference { TargetType = typeName, Access = accessKind ?? "ReadWrite" });
            }
        }
        if (accessRefs.Count > 0)
            typeInfo.AccessReferences = accessRefs;
    }

    private static void ExtractRelatedSystems(INamedTypeSymbol commandSymbol, TypeMetadata typeInfo)
    {
        var systemNames = new HashSet<string>();
        foreach (var member in commandSymbol.GetMembers())
        {
            if (member is IFieldSymbol field)
            {
                var fullTypeName = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (fullTypeName.Contains("EntitySystem"))
                    systemNames.Add(fullTypeName);
                var baseType = field.Type.BaseType;
                while (baseType != null)
                {
                    var baseName = baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (baseName.Contains("EntitySystem"))
                        systemNames.Add(baseName);
                    baseType = baseType.BaseType;
                }
            }
            if (member is IMethodSymbol method)
            {
                var isImpl = method.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == "CommandImplementationAttribute");
                if (!isImpl) continue;
                var retName = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (retName.Contains("EntitySystem"))
                    systemNames.Add(retName);
                foreach (var param in method.Parameters)
                {
                    var paramName = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (paramName.Contains("EntitySystem"))
                        systemNames.Add(paramName);
                }
            }
        }
        if (systemNames.Count > 0)
            typeInfo.RelatedSystems = systemNames.ToList();
    }

    // ==================== Reflection internals ====================

    private static TypeMetadata ExtractTypeMetadataReflection(System.Type type, System.Xml.Linq.XDocument? xmlDoc)
    {
        var typeInfo = new TypeMetadata
        {
            FullName = type.FullName ?? type.Name,
            Name = type.Name,
            Namespace = type.Namespace ?? "",
            AssemblyName = type.Assembly.GetName().Name ?? "",
            Summary = GetXmlDocSummaryReflection(type, xmlDoc),
            SeeAlso = GetXmlDocSeeAlsoReflection(type, xmlDoc),
        };

        foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
        {
            if (field.IsLiteral) continue;
            typeInfo.Fields.Add(new FieldMetadata
            {
                Name = field.Name,
                Type = field.FieldType.FullName ?? field.FieldType.Name,
                Summary = GetXmlDocSummaryReflection(field, xmlDoc),
                SeeAlso = GetXmlDocSeeAlsoReflection(field, xmlDoc),
            });
        }

        foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            typeInfo.Properties.Add(new PropertyMetadata
            {
                Name = prop.Name,
                Type = prop.PropertyType.FullName ?? prop.PropertyType.Name,
                Summary = GetXmlDocSummaryReflection(prop, xmlDoc),
                SeeAlso = GetXmlDocSeeAlsoReflection(prop, xmlDoc),
            });
        }

        foreach (var nestedType in type.GetNestedTypes(System.Reflection.BindingFlags.Public))
        {
            var nestedInfo = ExtractTypeMetadataReflection(nestedType, xmlDoc);
            if (!string.IsNullOrEmpty(nestedInfo.Summary) || nestedInfo.Fields.Count > 0 || nestedInfo.Properties.Count > 0)
                typeInfo.NestedTypes.Add(nestedInfo);
        }
        return typeInfo;
    }

    private static string? GetXmlDocSummaryReflection(System.Reflection.MemberInfo member, System.Xml.Linq.XDocument? xmlDoc)
    {
        if (xmlDoc == null) return null;
        try
        {
            var memberName = member switch
            {
                System.Type t => $"T:{t.FullName}",
                System.Reflection.FieldInfo f => $"F:{f.DeclaringType?.FullName}.{f.Name}",
                System.Reflection.PropertyInfo p => $"P:{p.DeclaringType?.FullName}.{p.Name}",
                _ => null
            };
            if (memberName == null) return null;
            foreach (var memberEl in xmlDoc.Root?.Element("members")?.Elements("member") ?? Enumerable.Empty<System.Xml.Linq.XElement>())
            {
                if (memberEl.Attribute("name")?.Value == memberName)
                {
                    var value = memberEl.Element("summary")?.Value?.Trim();
                    return string.IsNullOrEmpty(value) ? null : value;
                }
            }
        }
        catch { }
        return null;
    }

    private static List<string>? GetXmlDocSeeAlsoReflection(System.Reflection.MemberInfo member, System.Xml.Linq.XDocument? xmlDoc)
    {
        if (xmlDoc == null) return null;
        try
        {
            var memberName = member switch
            {
                System.Type t => $"T:{t.FullName}",
                System.Reflection.FieldInfo f => $"F:{f.DeclaringType?.FullName}.{f.Name}",
                System.Reflection.PropertyInfo p => $"P:{p.DeclaringType?.FullName}.{p.Name}",
                _ => null
            };
            if (memberName == null) return null;
            foreach (var memberEl in xmlDoc.Root?.Element("members")?.Elements("member") ?? Enumerable.Empty<System.Xml.Linq.XElement>())
            {
                if (memberEl.Attribute("name")?.Value == memberName)
                {
                    var refs = new List<string>();
                    foreach (var seeEl in memberEl.Descendants("see").Concat(memberEl.Descendants("seealso")))
                    {
                        var cref = seeEl.Attribute("cref")?.Value;
                        if (!string.IsNullOrEmpty(cref)) refs.Add(cref);
                    }
                    return refs.Count > 0 ? refs : null;
                }
            }
        }
        catch { }
        return null;
    }

    // ==================== Shared helpers ====================

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceOrTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                foreach (var type in GetAllTypes(ns))
                    yield return type;
            }
            else if (member is INamedTypeSymbol namedType)
            {
                yield return namedType;
                foreach (var nested in namedType.GetTypeMembers())
                {
                    if (nested.DeclaredAccessibility == Accessibility.Public || nested.DeclaredAccessibility == Accessibility.Internal)
                        yield return nested;
                }
            }
        }
    }

    private static bool IsDerivedFrom(INamedTypeSymbol? type, INamedTypeSymbol baseType)
    {
        if (type == null) return false;
        var current = type;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }
        return false;
    }
}
