using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using LiteDB.Vector;
using Xunit;

namespace LiteDB.Tests.Client
{
    public class VectorOptionalityTests
    {
        [Fact]
        public void LiteDB_public_surface_should_match_vector_allow_list()
        {
            var liteDbAssembly = typeof(LiteDatabase).Assembly;
            var exposures = VectorApiInspector.GetVectorSymbols(liteDbAssembly);

            exposures.Should().BeEmpty("vector APIs are isolated to the LiteDB.Vector plugin");
        }

        [Fact]
        public void LiteDBVector_plugin_should_expose_vector_extensions()
        {
            var pluginAssembly = typeof(LiteCollectionVectorExtensions).Assembly;
            var exposures = VectorApiInspector.GetVectorSymbols(pluginAssembly);

            exposures.Should().Contain("type::LiteDB.Vector.LiteCollectionVectorExtensions");
            exposures.Should().Contain("type::LiteDB.Vector.LiteRepositoryVectorExtensions");
            exposures.Should().Contain("type::LiteDB.Vector.VectorSearchPlugin");
            exposures.Should().Contain("type::LiteDB.Vector.Document.BsonVector");
        }

        private static class VectorApiInspector
        {
            public static IReadOnlyCollection<string> GetVectorSymbols(Assembly assembly)
            {
                var bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                var results = new HashSet<string>(StringComparer.Ordinal);

                foreach (var type in assembly.GetExportedTypes())
                {
                    var typeName = type.FullName;
                    if (!string.IsNullOrEmpty(typeName) && typeName.Contains("Vector", StringComparison.Ordinal))
                    {
                        results.Add($"type::{typeName}");
                    }

                    foreach (var method in type.GetMethods(bindingFlags))
                    {
                        if (method.IsSpecialName)
                        {
                            continue;
                        }

                        if (method.Name.Contains("Vector", StringComparison.Ordinal))
                        {
                            results.Add($"method::{typeName}.{method.Name}");
                        }
                    }

                    foreach (var property in type.GetProperties(bindingFlags))
                    {
                        if (property.Name.Contains("Vector", StringComparison.Ordinal))
                        {
                            results.Add($"property::{typeName}.{property.Name}");
                        }
                    }

                    foreach (var field in type.GetFields(bindingFlags))
                    {
                        if (field.IsSpecialName)
                        {
                            continue;
                        }

                        if (field.Name.Contains("Vector", StringComparison.Ordinal))
                        {
                            results.Add($"field::{typeName}.{field.Name}");
                        }
                    }

                    foreach (var @event in type.GetEvents(bindingFlags))
                    {
                        if (@event.Name.Contains("Vector", StringComparison.Ordinal))
                        {
                            results.Add($"event::{typeName}.{@event.Name}");
                        }
                    }
                }

                return results;
            }
        }
    }
}
