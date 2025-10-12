using System;
namespace LiteDB.Spatial.Core.Tests;

/// <summary>
/// Provides a lightweight category marker matching NUnit-style semantics.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class CategoryAttribute : Attribute
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
