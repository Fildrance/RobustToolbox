using Microsoft.CodeAnalysis;
using System.Collections.Generic;

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
}
