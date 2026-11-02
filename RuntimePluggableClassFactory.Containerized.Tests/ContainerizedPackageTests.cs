using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.Containerized.Implementations;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.RuntimePluggableClassFactory.Containerized;
using DevelApp.RuntimePluggableClassFactory.Containerized.Security;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.SemanticVersioning;
using DevelApp.Utility.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Containerized.Tests
{
    /// <summary>
    /// Tests for the shipped RuntimePluggableClassFactory.Containerized package:
    /// ContainerizedPluginLoader, ContainerizedPluginProxy, HybridPluginFactory and
    /// NuGetSignatureValidator, exercised against in-memory fakes (no network, no containers).
    /// </summary>
    [TestClass]
    public class ContainerizedPackageTests
    {
        private const string TestNamespace = "DevelApp.Test";
        private const string TestPluginName = "MyPlugin";

        private static PluginIdentifier CreatePluginId(string ns = TestNamespace, string name = TestPluginName, int major = 1, int minor = 0, int patch = 0)
        {
            return new PluginIdentifier
            {
                Namespace = new NamespaceString(ns),
                Name = new IdentifierString(name),
                Version = new SemanticVersionNumber(major, minor, patch)
            };
        }

        private static ContainerizedPluginInfo CreatePluginInfo(PluginIdentifier pluginId, string description = "Test plugin")
        {
            return new ContainerizedPluginInfo
            {
                PluginId = pluginId,
                Description = description,
                Status = PluginDeploymentStatus.Deployed,
                DeployedAt = DateTime.UtcNow
            };
        }

        // ------------------------------------------------------------------
        // ContainerizedPluginLoader<T>
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task Loader_LoadPluginsAsync_FindsDeployedPlugin()
        {
            var pluginId = CreatePluginId();
            var orchestrator = new FakeOrchestrator().Add(CreatePluginInfo(pluginId, "A deployed plugin"));
            using var loader = new ContainerizedPluginLoader<IContainerTestPlugin>(orchestrator, NullLogger<ContainerizedPluginLoader<IContainerTestPlugin>>.Instance);

            var allowed = new List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)>
            {
                (new NamespaceString(TestNamespace), new IdentifierString(TestPluginName), new SemanticVersionNumber(1, 0, 0))
            };

            var result = await loader.LoadPluginsAsync(allowed);

            Assert.AreEqual(1, result.Count());
            var plugin = result.Single();
            Assert.AreEqual("DevelApp.Test", plugin.ModuleName.ToString());
            Assert.AreEqual("MyPlugin", plugin.PluginName.ToString());
            Assert.AreEqual(1, plugin.Version.Major);
            Assert.AreEqual(0, plugin.Version.Minor);
            Assert.AreEqual(0, plugin.Version.Patch);
            Assert.AreEqual("A deployed plugin", plugin.Description);
            Assert.AreEqual(typeof(ContainerizedPluginProxy<IContainerTestPlugin>), plugin.Type);
        }

        [TestMethod]
        public async Task Loader_LoadPluginsAsync_NotFound_RaisesPluginLoadingFailed()
        {
            var orchestrator = new FakeOrchestrator(); // nothing deployed
            using var loader = new ContainerizedPluginLoader<IContainerTestPlugin>(orchestrator, NullLogger<ContainerizedPluginLoader<IContainerTestPlugin>>.Instance);

            var loadingFailedRaised = false;
            PluginLoadingErrorEventArgs? failureArgs = null;
            loader.PluginLoadingFailed += (sender, args) =>
            {
                loadingFailedRaised = true;
                failureArgs = args;
            };

            var allowed = new List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)>
            {
                (new NamespaceString(TestNamespace), new IdentifierString(TestPluginName), new SemanticVersionNumber(9, 9, 9))
            };

            var result = await loader.LoadPluginsAsync(allowed);

            Assert.AreEqual(0, result.Count());
            Assert.IsTrue(loadingFailedRaised);
            Assert.IsNotNull(failureArgs);
            Assert.AreEqual("MyPlugin", failureArgs!.PluginName?.ToString());
            Assert.IsFalse(string.IsNullOrEmpty(failureArgs.Message));
            Assert.IsTrue(failureArgs.Message.Contains("not found"));
        }

        [TestMethod]
        public async Task Loader_ListAllPossiblePluginsAsync_ReturnsAllDeployedPlugins()
        {
            var orchestrator = new FakeOrchestrator()
                .Add(CreatePluginInfo(CreatePluginId(name: "PluginA"), "A"))
                .Add(CreatePluginInfo(CreatePluginId(name: "PluginB"), "B"));
            using var loader = new ContainerizedPluginLoader<IContainerTestPlugin>(orchestrator, NullLogger<ContainerizedPluginLoader<IContainerTestPlugin>>.Instance);

            var result = await loader.ListAllPossiblePluginsAsync();

            Assert.AreEqual(2, result.Count());
            CollectionAssert.AreEquivalent(
                new[] { "PluginA", "PluginB" },
                result.Select(p => p.PluginName.ToString()).ToArray());
        }

        [TestMethod]
        public async Task Loader_ListPluginsByVersionRangeAsync_FiltersVersions()
        {
            var orchestrator = new FakeOrchestrator()
                .Add(CreatePluginInfo(CreatePluginId(major: 1, minor: 0, patch: 0)))
                .Add(CreatePluginInfo(CreatePluginId(major: 2, minor: 0, patch: 0)));
            using var loader = new ContainerizedPluginLoader<IContainerTestPlugin>(orchestrator, NullLogger<ContainerizedPluginLoader<IContainerTestPlugin>>.Instance);

            var result = await loader.ListPluginsByVersionRangeAsync(VersionRange.Parse(">=1.5.0"));

            Assert.AreEqual(1, result.Count());
            Assert.AreEqual(2, result.Single().Version.Major);
        }

        [TestMethod]
        public async Task Loader_ListPluginsByModuleAsync_FiltersModule()
        {
            var orchestrator = new FakeOrchestrator()
                .Add(CreatePluginInfo(CreatePluginId(ns: "DevelApp.ModuleA", name: "PluginA")))
                .Add(CreatePluginInfo(CreatePluginId(ns: "DevelApp.ModuleB", name: "PluginB")));
            using var loader = new ContainerizedPluginLoader<IContainerTestPlugin>(orchestrator, NullLogger<ContainerizedPluginLoader<IContainerTestPlugin>>.Instance);

            var result = await loader.ListPluginsByModuleAsync(new NamespaceString("DevelApp.ModuleA"));

            Assert.AreEqual(1, result.Count());
            Assert.AreEqual("PluginA", result.Single().PluginName.ToString());
        }

        [TestMethod]
        public async Task Loader_UnloadPlugin_ValidIdentifier_UndeploysPlugin()
        {
            var pluginId = CreatePluginId();
            var orchestrator = new FakeOrchestrator().Add(CreatePluginInfo(pluginId));
            using var loader = new ContainerizedPluginLoader<IContainerTestPlugin>(orchestrator, NullLogger<ContainerizedPluginLoader<IContainerTestPlugin>>.Instance);

            // Format: namespace.name@version
            var unloaded = loader.UnloadPlugin("DevelApp.Test.MyPlugin@1.0.0");

            Assert.IsTrue(unloaded);
            // Undeploy runs fire-and-forget, wait for it to complete
            await WaitForAsync(() => orchestrator.Undeployed.Count == 1);
            Assert.AreEqual("DevelApp.Test.MyPlugin@1.0.0", orchestrator.Undeployed.Single().ToString());
        }

        [TestMethod]
        public void Loader_UnloadPlugin_InvalidIdentifier_ReturnsFalse()
        {
            var orchestrator = new FakeOrchestrator();
            using var loader = new ContainerizedPluginLoader<IContainerTestPlugin>(orchestrator, NullLogger<ContainerizedPluginLoader<IContainerTestPlugin>>.Instance);

            // No '@' separator and no namespace.name dot format
            Assert.IsFalse(loader.UnloadPlugin("no-version"));
            Assert.IsFalse(loader.UnloadPlugin("onlyname@1.0.0"));
            Assert.IsFalse(loader.UnloadPlugin(null));
            Assert.IsFalse(loader.UnloadPlugin("DevelApp.Test.MyPlugin@not-a-version"));
            Assert.AreEqual(0, orchestrator.Undeployed.Count);
        }

        [TestMethod]
        public async Task Loader_UnloadAllPlugins_UndeploysEverything()
        {
            var orchestrator = new FakeOrchestrator()
                .Add(CreatePluginInfo(CreatePluginInfo(CreatePluginId(name: "PluginA")).PluginId))
                .Add(CreatePluginInfo(CreatePluginId(name: "PluginB")));
            using var loader = new ContainerizedPluginLoader<IContainerTestPlugin>(orchestrator, NullLogger<ContainerizedPluginLoader<IContainerTestPlugin>>.Instance);

            loader.UnloadAllPlugins();

            // Undeploy runs fire-and-forget, wait for it to complete
            await WaitForAsync(() => orchestrator.Undeployed.Count == 2);
        }

        [TestMethod]
        public void Loader_Dispose_DisposesDisposableOrchestrator()
        {
            var orchestrator = new FakeOrchestrator();
            var loader = new ContainerizedPluginLoader<IContainerTestPlugin>(orchestrator, NullLogger<ContainerizedPluginLoader<IContainerTestPlugin>>.Instance);

            loader.Dispose();

            Assert.IsTrue(orchestrator.Disposed);
        }

        [TestMethod]
        public void Loader_Constructor_NullArguments_Throw()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new ContainerizedPluginLoader<IContainerTestPlugin>(null!, NullLogger<ContainerizedPluginLoader<IContainerTestPlugin>>.Instance));
            Assert.ThrowsException<ArgumentNullException>(() =>
                new ContainerizedPluginLoader<IContainerTestPlugin>(new FakeOrchestrator(), null!));
        }

        // ------------------------------------------------------------------
        // ContainerizedPluginProxy<T>
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task Proxy_ExecuteAsync_MapsRequestAndSuccessfulResult()
        {
            var pluginId = CreatePluginId();
            var orchestrator = new FakeOrchestrator
            {
                ExecuteHandler = request => new ContainerizedPluginExecutionResult
                {
                    Success = true,
                    Data = "CookieMonster",
                    ExecutionId = "exec-1"
                }
            };
            var proxy = new ContainerizedPluginProxy<IContainerTestPlugin>(
                orchestrator, pluginId, CreatePluginInfo(pluginId, "Desc"));

            var result = await proxy.ExecuteAsync(new TestExecutionContext(), "Monster");

            Assert.IsTrue(result.Success);
            Assert.AreEqual("CookieMonster", result.Data);
            // The request sent to the orchestrator carries the plugin id and serialized input
            var request = orchestrator.Executed.Single();
            Assert.AreEqual("DevelApp.Test.MyPlugin@1.0.0", request.PluginId.ToString());
            Assert.AreEqual("Monster", request.InputData);
        }

        [TestMethod]
        public async Task Proxy_ExecuteAsync_MapsFailedResult()
        {
            var pluginId = CreatePluginId();
            var orchestrator = new FakeOrchestrator
            {
                ExecuteHandler = _ => new ContainerizedPluginExecutionResult
                {
                    Success = false,
                    ErrorMessage = "container crashed",
                    Exception = new InvalidOperationException("boom")
                }
            };
            var proxy = new ContainerizedPluginProxy<IContainerTestPlugin>(
                orchestrator, pluginId, CreatePluginInfo(pluginId));

            var result = await proxy.ExecuteAsync(new TestExecutionContext(), "input");

            Assert.IsFalse(result.Success);
            Assert.AreEqual("container crashed", result.ErrorMessage);
            Assert.IsNotNull(result.Exception);
        }

        [TestMethod]
        public async Task Proxy_ExecuteAsync_OrchestratorThrows_ReturnsFailure()
        {
            var pluginId = CreatePluginId();
            var orchestrator = new FakeOrchestrator
            {
                ExecuteHandler = _ => throw new TimeoutException("timed out")
            };
            var proxy = new ContainerizedPluginProxy<IContainerTestPlugin>(
                orchestrator, pluginId, CreatePluginInfo(pluginId));

            var result = await proxy.ExecuteAsync(new TestExecutionContext(), "input");

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorMessage!.Contains("timed out"));
        }

        [TestMethod]
        public void Proxy_Properties_ExposePluginIdentity()
        {
            var pluginId = CreatePluginId(ns: "MyNs", name: "MyName", major: 2, minor: 3, patch: 4);
            var proxy = new ContainerizedPluginProxy<IContainerTestPlugin>(
                new FakeOrchestrator(), pluginId, CreatePluginInfo(pluginId, "The description"));

            Assert.AreEqual("MyName", proxy.Name.ToString());
            Assert.AreEqual("MyNs", proxy.Module.ToString());
            Assert.AreEqual("2.3.4", proxy.Version.ToString());
            Assert.AreEqual("The description", proxy.Description);
        }

        [TestMethod]
        public void Proxy_ExecuteTyped_Sync_WrapsAsyncResult()
        {
            var pluginId = CreatePluginId();
            var orchestrator = new FakeOrchestrator
            {
                ExecuteHandler = _ => new ContainerizedPluginExecutionResult { Success = true, Data = "42" }
            };
            var proxy = new ContainerizedPluginProxy<IContainerTestPlugin>(
                orchestrator, pluginId, CreatePluginInfo(pluginId));

            var result = proxy.ExecuteTyped(new TestExecutionContext(), "input");

            Assert.IsTrue(result.Success);
            // Valid JSON payloads are deserialized into JsonElement; compare the value, not the CLR type
            Assert.AreEqual("42", result.Data!.ToString());
        }

        // ------------------------------------------------------------------
        // HybridPluginFactory<T>
        // ------------------------------------------------------------------

        [TestMethod]
        public void HybridFactory_Constructor_BothFactoriesNull_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                new HybridPluginFactory<IPluginClass>(null, null, NullLogger<HybridPluginFactory<IPluginClass>>.Instance));
        }

        [TestMethod]
        public async Task HybridFactory_GetPluginAsync_ContainerizedMode_ReturnsProxy()
        {
            var pluginId = CreatePluginId(ns: "HybridNs", name: "HybridPlugin");
            var orchestrator = new FakeOrchestrator().Add(CreatePluginInfo(pluginId));
            var factory = new HybridPluginFactory<IPluginClass>(
                null, orchestrator, NullLogger<HybridPluginFactory<IPluginClass>>.Instance);

            var plugin = await factory.GetPluginAsync(
                new NamespaceString("HybridNs"),
                new IdentifierString("HybridPlugin"),
                executionMode: PluginExecutionMode.Containerized);

            Assert.IsNotNull(plugin);
            Assert.AreEqual("HybridPlugin", plugin!.Name.ToString());
            Assert.AreEqual("HybridNs", plugin.Module.ToString());
        }

        [TestMethod]
        public async Task HybridFactory_GetPluginAsync_TraditionalMode_UsesTraditionalFactory()
        {
            var pluginLoader = new Mock<IPluginLoader<IPluginClass>>();
            var traditionalPlugin = new TraditionalTestPlugin();
            pluginLoader
                .Setup(l => l.LoadPluginsAsync(It.IsAny<List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)>>()))
                .ReturnsAsync(new List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>
                {
                    (new NamespaceString("Trad"), new IdentifierString("Legacy"), new SemanticVersionNumber(1, 0, 0), "Traditional", typeof(TraditionalTestPlugin))
                });
            var traditionalFactory = new PluginClassFactory<IPluginClass>(pluginLoader.Object);
            var factory = new HybridPluginFactory<IPluginClass>(
                traditionalFactory, new FakeOrchestrator(), NullLogger<HybridPluginFactory<IPluginClass>>.Instance);
            factory.AllowTraditionalPlugin(new NamespaceString("Trad"), new IdentifierString("Legacy"), new SemanticVersionNumber(1, 0, 0));
            await factory.RefreshTraditionalPluginsAsync();

            var plugin = await factory.GetPluginAsync(
                new NamespaceString("Trad"),
                new IdentifierString("Legacy"),
                executionMode: PluginExecutionMode.Traditional);

            Assert.IsNotNull(plugin);
            Assert.IsInstanceOfType(plugin, typeof(TraditionalTestPlugin));
        }

        [TestMethod]
        public async Task HybridFactory_GetPluginAsync_AutoMode_FallsBackToTraditional()
        {
            // Nothing deployed containerized; PreferContainerized = true (default) must fall back to traditional
            var pluginLoader = new Mock<IPluginLoader<IPluginClass>>();
            pluginLoader
                .Setup(l => l.LoadPluginsAsync(It.IsAny<List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)>>()))
                .ReturnsAsync(new List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>
                {
                    (new NamespaceString("Trad"), new IdentifierString("Legacy"), new SemanticVersionNumber(1, 0, 0), "Traditional", typeof(TraditionalTestPlugin))
                });
            var traditionalFactory = new PluginClassFactory<IPluginClass>(pluginLoader.Object);
            var factory = new HybridPluginFactory<IPluginClass>(
                traditionalFactory, new FakeOrchestrator(), NullLogger<HybridPluginFactory<IPluginClass>>.Instance);
            factory.AllowTraditionalPlugin(new NamespaceString("Trad"), new IdentifierString("Legacy"), new SemanticVersionNumber(1, 0, 0));
            await factory.RefreshTraditionalPluginsAsync();

            var plugin = await factory.GetPluginAsync(
                new NamespaceString("Trad"),
                new IdentifierString("Legacy"),
                new SemanticVersionNumber(1, 0, 0));

            Assert.IsNotNull(plugin);
            Assert.IsInstanceOfType(plugin, typeof(TraditionalTestPlugin));
        }

        [TestMethod]
        public async Task HybridFactory_ListAvailablePluginsAsync_ReportsBothSources()
        {
            var pluginLoader = new Mock<IPluginLoader<IPluginClass>>();
            pluginLoader
                .Setup(l => l.ListAllPossiblePluginsAsync())
                .ReturnsAsync(new List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>
                {
                    (new NamespaceString("Trad"), new IdentifierString("Legacy"), new SemanticVersionNumber(1, 0, 0), "Traditional", typeof(TraditionalTestPlugin))
                });
            var traditionalFactory = new PluginClassFactory<IPluginClass>(pluginLoader.Object);
            var orchestrator = new FakeOrchestrator().Add(CreatePluginInfo(CreatePluginId(ns: "HybridNs", name: "HybridPlugin")));
            var factory = new HybridPluginFactory<IPluginClass>(
                traditionalFactory, orchestrator, NullLogger<HybridPluginFactory<IPluginClass>>.Instance);

            var plugins = (await factory.ListAvailablePluginsAsync()).ToList();

            Assert.AreEqual(2, plugins.Count);
            var traditional = plugins.Single(p => p.ExecutionMode == PluginExecutionMode.Traditional);
            var containerized = plugins.Single(p => p.ExecutionMode == PluginExecutionMode.Containerized);
            Assert.AreEqual("Legacy", traditional.PluginName.ToString());
            Assert.AreEqual("HybridPlugin", containerized.PluginName.ToString());
        }

        // ------------------------------------------------------------------
        // NuGetSignatureValidator
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task SignatureValidator_ValidatePackageAsync_InvalidPackage_ReturnsInvalid()
        {
            var validator = new NuGetSignatureValidator(
                NullLogger<NuGetSignatureValidator>.Instance,
                new Mock<ITrustedSignersRepository>().Object);

            using var notAPackage = new MemoryStream(Encoding.UTF8.GetBytes("this is definitely not a zip archive"));
            var result = await validator.ValidatePackageAsync(notAPackage, new PackageValidationOptions());

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any());
        }

        [TestMethod]
        public void SignatureValidator_Constructor_NullArguments_Throw()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new NuGetSignatureValidator(null!, new Mock<ITrustedSignersRepository>().Object));
            Assert.ThrowsException<ArgumentNullException>(() =>
                new NuGetSignatureValidator(NullLogger<NuGetSignatureValidator>.Instance, null!));
        }

        [TestMethod]
        public async Task SignatureValidator_TrustedSigners_DelegateToRepository()
        {
            var repository = new Mock<ITrustedSignersRepository>();
            var signer = new TrustedSigner { SignerName = "DevelApp", CertificateThumbprint = "abc123" };
            repository.Setup(r => r.GetTrustedSignersAsync()).ReturnsAsync(new[] { signer });
            var validator = new NuGetSignatureValidator(
                NullLogger<NuGetSignatureValidator>.Instance, repository.Object);

            await validator.AddTrustedSignerAsync(signer);
            await validator.RemoveTrustedSignerAsync("abc123");

            var signers = (await validator.GetTrustedSignersAsync()).ToList();

            repository.Verify(r => r.AddTrustedSignerAsync(signer), Times.Once);
            repository.Verify(r => r.RemoveTrustedSignerAsync("abc123"), Times.Once);
            Assert.AreEqual(1, signers.Count);
            Assert.AreEqual("DevelApp", signers[0].SignerName);
        }

        // ------------------------------------------------------------------
        // Test helpers
        // ------------------------------------------------------------------

        private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                {
                    Assert.Fail("Timed out waiting for background operation to complete");
                }
                await Task.Delay(25);
            }
        }

        /// <summary>
        /// In-memory IContainerizedPluginOrchestrator fake: no containers, no network.
        /// </summary>
        private sealed class FakeOrchestrator : IContainerizedPluginOrchestrator, IDisposable
        {
            private readonly List<ContainerizedPluginInfo> _plugins = new();

            public List<PluginIdentifier> Undeployed { get; } = new();
            public List<ContainerizedPluginExecutionRequest> Executed { get; } = new();
            public Func<ContainerizedPluginExecutionRequest, ContainerizedPluginExecutionResult>? ExecuteHandler { get; set; }
            public bool Disposed { get; private set; }

            public event EventHandler<PluginEventArgs>? PluginDeployed { add { } remove { } }
            public event EventHandler<PluginEventArgs>? PluginExecuted { add { } remove { } }
            public event EventHandler<PluginEventArgs>? PluginFailed { add { } remove { } }
            public event EventHandler<PluginEventArgs>? PluginUndeployed { add { } remove { } }

            public FakeOrchestrator Add(ContainerizedPluginInfo pluginInfo)
            {
                _plugins.Add(pluginInfo);
                return this;
            }

            public Task<PluginDeploymentResult> DeployPluginAsync(PluginDeploymentRequest request, CancellationToken cancellationToken = default)
                => Task.FromResult(new PluginDeploymentResult { Success = true, DeploymentId = "deploy-1" });

            public Task<ContainerizedPluginExecutionResult> ExecutePluginAsync(ContainerizedPluginExecutionRequest request, CancellationToken cancellationToken = default)
            {
                Executed.Add(request);
                return Task.FromResult(ExecuteHandler != null
                    ? ExecuteHandler(request)
                    : new ContainerizedPluginExecutionResult { Success = true });
            }

            public Task<ContainerizedPluginInfo?> GetPluginInfoAsync(PluginIdentifier pluginId, CancellationToken cancellationToken = default)
                => Task.FromResult(_plugins.FirstOrDefault(p =>
                    p.PluginId.Namespace.Equals(pluginId.Namespace) &&
                    p.PluginId.Name.Equals(pluginId.Name)));

            public Task<IEnumerable<ContainerizedPluginInfo>> ListPluginsAsync(PluginListOptions? options = null, CancellationToken cancellationToken = default)
            {
                // Return a copy: the loader undeploys (mutates) while iterating the result
                IEnumerable<ContainerizedPluginInfo> result = _plugins.ToList();
                if (options?.StatusFilter.HasValue == true)
                {
                    result = result.Where(p => p.Status == options.StatusFilter);
                }
                return Task.FromResult(result);
            }

            public Task UndeployPluginAsync(PluginIdentifier pluginId, CancellationToken cancellationToken = default)
            {
                Undeployed.Add(pluginId);
                _plugins.RemoveAll(p =>
                    p.PluginId.Namespace.Equals(pluginId.Namespace) &&
                    p.PluginId.Name.Equals(pluginId.Name) &&
                    p.PluginId.Version.Equals(pluginId.Version));
                return Task.CompletedTask;
            }

            public Task<PluginHealth> GetPluginHealthAsync(PluginIdentifier pluginId, CancellationToken cancellationToken = default)
                => Task.FromResult(new PluginHealth { IsHealthy = true, Status = "Healthy" });

            public Task NotifyPluginUpdateAsync(PluginIdentifier pluginId, SemanticVersionNumber newVersion, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public void Dispose() => Disposed = true;
        }

        private sealed class TestExecutionContext : IPluginExecutionContext
        {
            public IPluginLogger? Logger => null;
            public CancellationToken CancellationToken => CancellationToken.None;
            public IReadOnlyDictionary<string, object>? Properties => null;
        }

        /// <summary>
        /// Plugin interface used as T for loader/proxy tests.
        /// </summary>
        public interface IContainerTestPlugin : IPluginClass
        {
        }

        private sealed class TraditionalTestPlugin : IPluginClass
        {
            public IdentifierString Name => new IdentifierString("Legacy");
            public NamespaceString Module => new NamespaceString("Trad");
            public string? Description => "Traditional plugin";
            public SemanticVersionNumber Version => new SemanticVersionNumber(1, 0, 0);
        }
    }
}
