#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace LiteDB.Spatial.Core.Tests;

/// <summary>
/// Provides a convenient trait alias that mirrors NUnit-style categories.
/// </summary>
[TraitDiscoverer("LiteDB.Spatial.Core.Tests.CategoryDiscoverer", "LiteDB.Spatial.Core.Tests")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class CategoryAttribute : Attribute, ITraitAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CategoryAttribute"/> class.
    /// </summary>
    /// <param name="category">The category name applied to the test.</param>
    public CategoryAttribute(string category)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
    }

    /// <summary>
    /// Gets the category name associated with the test.
    /// </summary>
    public string Category { get; }
}

internal sealed class CategoryDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        if (traitAttribute is null)
        {
            throw new ArgumentNullException(nameof(traitAttribute));
        }

        var category = traitAttribute.GetConstructorArguments().FirstOrDefault()?.ToString();

        if (string.IsNullOrWhiteSpace(category))
        {
            yield break;
        }

        yield return new KeyValuePair<string, string>("Category", category);
    }
}
