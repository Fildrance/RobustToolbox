using Microsoft.CodeAnalysis;
using Robust.MetadataGenerator.Models;
using System.Collections.Generic;
using System.Linq;

namespace Robust.MetadataGenerator;

public static class TypeMetadataExtractor
{
    // ==================== Roslyn internals ====================
    public static TypeMetadata ExtractTypeMetadata(this INamedTypeSymbol typeSymbol, string kind)
    {
        var doc = ParseDocumentation(typeSymbol);

        var typeInfo = new TypeMetadata
        {
            FullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Name = typeSymbol.Name,
            Namespace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "",
            AssemblyName = typeSymbol.ContainingAssembly?.Name ?? "",
            Summary = ElementText(doc, "summary"),
            ViewVariablesSummary = ElementText(doc, "ViewVariablesSummary"),
            SeeAlso = SeeAlsoRefs(doc),
            Kind = kind,
        };

        ExtractAccessReferences(typeSymbol, typeInfo);

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IFieldSymbol { DeclaredAccessibility: Accessibility.Public, IsConst: false, IsReadOnly: false } field)
            {
                var fieldDoc = ParseDocumentation(field);
                typeInfo.Fields.Add(new FieldMetadata
                {
                    Name = field.Name,
                    Type = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Summary = ElementText(fieldDoc, "summary"),
                    ViewVariablesSummary = ElementText(fieldDoc, "ViewVariablesSummary"),
                    SeeAlso = SeeAlsoRefs(fieldDoc),
                });
            }
            else if (member is IPropertySymbol { DeclaredAccessibility: Accessibility.Public, IsReadOnly: false, IsWriteOnly: false } prop)
            {
                var propDoc = ParseDocumentation(prop);
                typeInfo.Fields.Add(new FieldMetadata()
                {
                    Name = prop.Name,
                    Type = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Summary = ElementText(propDoc, "summary"),
                    ViewVariablesSummary = ElementText(propDoc, "ViewVariablesSummary"),
                    SeeAlso = SeeAlsoRefs(propDoc),
                });
            }
        }

        ExtractRelatedSystems(typeSymbol, typeInfo);
        return typeInfo;
    }

    private static System.Xml.Linq.XDocument? ParseDocumentation(ISymbol symbol)
    {
        var xmlDoc = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xmlDoc))
            return null;

        try
        {
            return System.Xml.Linq.XDocument.Parse(xmlDoc);
        }
        catch
        {
            return null;
        }
    }

    private static string? ElementText(System.Xml.Linq.XDocument? doc, string elementName)
    {
        var value = doc?.Root?.Element(elementName)?.Value;
        if (value == null)
            return null;

        value = System.Xml.XmlConvert.DecodeName(value);
        value = value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static List<string>? SeeAlsoRefs(System.Xml.Linq.XDocument? doc)
    {
        if (doc == null)
            return null;

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

    private static void ExtractAccessReferences(INamedTypeSymbol typeSymbol, TypeMetadata typeInfo)
    {
        var accessAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "AccessAttribute" or "Access");
        if (accessAttr == null)
            return;

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
                        //var typeName = element.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat); - returns just Object.Type
                        var typeName = element.Value?.ToString();
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
                var isImpl = method.GetAttributes()
                    .Any(a => a.AttributeClass?.Name == "CommandImplementationAttribute");
                if (!isImpl)
                    continue;

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
}
