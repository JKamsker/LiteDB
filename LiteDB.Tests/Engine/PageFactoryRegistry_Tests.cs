using System;
using System.Reflection;
using FluentAssertions;
using LiteDB;
using LiteDB.Engine;
using LiteDB.Plugins;
using LiteDB.Vector.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class PageFactoryRegistry_Tests
    {
        [Fact]
        public void VectorPagesWithoutPluginShouldEmitHelpfulDiagnostic()
        {
            ILitePluginContext pluginlessContext = CreatePluginlessContext();

            var buffer = new PageBuffer(new byte[Constants.PAGE_SIZE], 0, uniqueID: 0);
            _ = new VectorIndexPage(buffer, pageID: 7);

            Action act = () => BasePage.ReadPage<VectorIndexPage>(buffer, pluginlessContext);

            act.Should()
                .Throw<LiteException>()
                .Which.Message.Should().Contain("LiteDB.Vector");
        }

        private static ILitePluginContext CreatePluginlessContext()
        {
            var assembly = typeof(LiteDatabase).Assembly;
            var contextType = assembly.GetType("LiteDB.Plugins.DefaultPluginContext", throwOnError: true);
            var serviceProviderType = assembly.GetType("LiteDB.Plugins.NullServiceProvider", throwOnError: true);
            var loggerType = assembly.GetType("LiteDB.Plugins.NullLogger", throwOnError: true);

            var services = (IServiceProvider)serviceProviderType
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null);

            var logger = (ILogger)loggerType
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null);

            return (ILitePluginContext)Activator.CreateInstance(contextType, new ConnectionString(), services, logger)!;
        }
    }
}
