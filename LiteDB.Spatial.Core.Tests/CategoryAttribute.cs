using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace LiteDB.Spatial.Core.Tests;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
[TraitDiscoverer("LiteDB.Spatial.Core.Tests.CategoryDiscoverer", "LiteDB.Spatial.Core.Tests")]
public sealed class CategoryAttribute : Attribute, ITraitAttribute
{
    public CategoryAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }
}

public sealed class CategoryDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        var name = traitAttribute.GetConstructorArguments().FirstOrDefault() as string ?? string.Empty;
        yield return new KeyValuePair<string, string>("Category", name);
    }
}
