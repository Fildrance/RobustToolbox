using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace Robust.MetadataGenerator.Helpers;

public static class CodeAnalysisHelpers
{
    /// <summary>
    /// Extract all types and nested types from namespace or type, recursively.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> GetAllTypes(this INamespaceOrTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                foreach (var type in GetAllTypes(ns))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol namedType)
            {
                yield return namedType;
                foreach (var nested in namedType.GetTypeMembers())
                {
                    if (nested.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
                        yield return nested;
                }
            }
        }
    }

    /// <summary>
    /// Checks if type is derived from baseType using <seealso cref="SymbolDisplayFormat.FullyQualifiedFormat"/>.
    /// Supports both class inheritance and interface implementation.
    /// </summary>
    public static bool IsDerivedFrom(this INamedTypeSymbol? type, INamedTypeSymbol baseType)
    {
        if (type == null)
            return false;

        var fullName = baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var current = type;
        while (current != null)
        {
            var currentName = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (string.Equals(fullName, currentName))
                return true;

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Checks if type matches baseType through class inheritance or interface implementation.
    /// </summary>
    public static bool MatchesBaseType(this INamedTypeSymbol? type, INamedTypeSymbol baseType)
    {
        if (type == null)
            return false;

        var fullName = baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Check class inheritance
        var current = type;
        while (current != null)
        {
            var currentName = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (string.Equals(fullName, currentName))
                return true;

            current = current.BaseType;
        }

        // Check interface implementation
        foreach (var iface in type.AllInterfaces)
        {
            var ifaceName = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (string.Equals(fullName, ifaceName))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a symbol has an attribute with the given name (matching by short name or metadata name).
    /// </summary>
    public static bool HasAttribute(this ISymbol symbol, string attributeName)
    {
        return symbol.GetAttributes()
            .Any(a =>
                {
                    var className = a.AttributeClass?.Name;
                    var metadataName = a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                    return className == attributeName
                           || metadataName == attributeName
                           || metadataName == $"global::{attributeName}";
                }
            );
    }
}
