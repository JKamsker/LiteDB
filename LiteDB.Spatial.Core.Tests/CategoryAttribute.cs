using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace LiteDB.Spatial.Core.Tests;

/// <summary>
/// Provides a lightweight category marker matching NUnit-style semantics.
/// </summary>
[TraitDiscoverer(CategoryDiscoverer.TypeName, CategoryDiscoverer.AssemblyName)]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class CategoryAttribute : Attribute, ITraitAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CategoryAttribute"/> class.
    /// </summary>
    /// <param name="category">The category name to associate with the test.</param>
    public CategoryAttribute(string category)
    {
        Name = category;
    }

    /// <summary>
    /// Gets the category associated with the test.
    /// </summary>
    public string Name { get; }
}

/// <summary>
/// Discovers category traits and exposes them to xUnit's filtering infrastructure.
/// </summary>
public sealed class CategoryDiscoverer : ITraitDiscoverer
{
    /// <summary>
    /// The fully-qualified type name used by <see cref="TraitDiscovererAttribute"/>.
    /// </summary>
    public const string TypeName = "LiteDB.Spatial.Core.Tests.CategoryDiscoverer";

    /// <summary>
    /// The assembly name where the discoverer lives.
    /// </summary>
    public const string AssemblyName = "LiteDB.Spatial.Core.Tests";

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        var category = traitAttribute.GetNamedArgument<string>(nameof(CategoryAttribute.Name));

        if (string.IsNullOrWhiteSpace(category))
        {
            category = traitAttribute.GetConstructorArguments().FirstOrDefault() as string;
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            yield break;
        }

        yield return new KeyValuePair<string, string>("Category", category);

        if (!string.Equals(category, "spatial", StringComparison.OrdinalIgnoreCase))
        {
            yield return new KeyValuePair<string, string>("Category", "spatial");
        }
    }
}
