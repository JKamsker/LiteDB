using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

[TraitDiscoverer("LiteDB.Spatial.Core.Tests.TestSupport.CategoryDiscoverer", "LiteDB.Spatial.Core.Tests")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class CategoryAttribute : Attribute, ITraitAttribute
{
    public CategoryAttribute(string value)
    {
        Value = value;
    }

    public string Value { get; }
}

public sealed class CategoryDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        var argument = traitAttribute.GetConstructorArguments().FirstOrDefault()?.ToString() ?? string.Empty;
        yield return new KeyValuePair<string, string>("Category", argument);
    }
}
