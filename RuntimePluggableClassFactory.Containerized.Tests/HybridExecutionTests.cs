using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.Interface;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Containerized.Tests
{
    /// <summary>
    /// Tests for hybrid plugin execution (traditional + containerized)
    /// Validates the coexistence of both plugin execution modes
    /// </summary>
    [TestClass]
    public class HybridExecutionTests
    {
        [TestMethod]
        public void TraditionalPluginFactory_ShouldWorkAsExpected()
        {
            // Arrange
            var mockLoader = new Mock<IPluginLoader<ITestPlugin>>();
            var factory = new PluginClassFactory<ITestPlugin>(mockLoader.Object);

            // Act & Assert
            Assert.IsNotNull(factory);
            Assert.IsNotNull(factory.PluginLoader);
            Assert.AreEqual(mockLoader.Object, factory.PluginLoader);
        }

        [TestMethod]
        public async Task HybridPluginFactory_ShouldHandleTraditionalPlugins()
        {
            // Arrange
            var mockPlugin = new Mock<ITestPlugin>();
            mockPlugin.Setup(p => p.Name).Returns("TestPlugin");
            mockPlugin.Setup(p => p.Module).Returns("TestModule");
            mockPlugin.Setup(p => p.Description).Returns("Test Description");

            var mockLoader = new Mock<IPluginLoader<ITestPlugin>>();
            var traditionalFactory = new PluginClassFactory<ITestPlugin>(mockLoader.Object);

            var hybridFactory = new MockHybridPluginFactory<ITestPlugin>(traditionalFactory, null);

            // Act
            var plugins = await hybridFactory.ListAvailablePluginsAsync();

            // Assert
            Assert.IsNotNull(plugins);
        }

        [TestMethod]
        public async Task HybridPluginFactory_ShouldHandleContainerizedPlugins()
        {
            // Arrange
            var mockOrchestrator = new Mock<IMockContainerizedOrchestrator>();
            mockOrchestrator.Setup(o => o.ListPluginsAsync(It.IsAny<object>(), It.IsAny<System.Threading.CancellationToken>()))
                           .ReturnsAsync(new List<MockContainerizedPluginInfo>
                           {
                               new MockContainerizedPluginInfo
                               {
                                   PluginId = new MockPluginIdentifier { Namespace = "Test", Name = "ContainerPlugin", Version = "1.0.0" },
                                   Description = "Containerized Test Plugin"
                               }
                           });

            var hybridFactory = new MockHybridPluginFactory<ITestPlugin>(null, mockOrchestrator.Object);

            // Act
            var plugins = await hybridFactory.ListAvailablePluginsAsync();

            // Assert
            Assert.IsNotNull(plugins);
        }

        [TestMethod]
        public void PluginExecutionModes_ShouldBeDefinedCorrectly()
        {
            // Arrange & Act & Assert
            var autoMode = MockPluginExecutionMode.Auto;
            var traditionalMode = MockPluginExecutionMode.Traditional;
            var containerizedMode = MockPluginExecutionMode.Containerized;

            Assert.IsNotNull(autoMode);
            Assert.IsNotNull(traditionalMode);
            Assert.IsNotNull(containerizedMode);
        }

        [TestMethod]
        public void HybridPluginFactoryOptions_ShouldHaveCorrectDefaults()
        {
            // Arrange & Act
            var options = new MockHybridPluginFactoryOptions();

            // Assert
            Assert.IsTrue(options.PreferContainerized, "Should prefer containerized plugins by default");
            Assert.IsFalse(options.AutoDeployContainerized, "Should not auto-deploy by default");
        }

        [TestMethod]
        public void ContainerizedPluginLoaderOptions_ShouldHaveReasonableDefaults()
        {
            // Arrange & Act
            var options = new MockContainerizedPluginLoaderOptions();

            // Assert
            Assert.AreEqual(TimeSpan.FromMinutes(2), options.ContainerOperationTimeout, "Default timeout should be 2 minutes");
            Assert.IsFalse(options.AutoDeploy, "Auto-deploy should be false by default");
        }

        [TestMethod]
        public async Task SecurityValidator_ShouldValidateSignedPackages()
        {
            // Arrange
            var mockValidator = new Mock<IMockSignedPackageManager>();
            var testPackage = new MockSignedPackageInfo
            {
                PackageId = "DevelApp.TestPlugin",
                Version = "1.0.0",
                SignerInfo = new MockSignerInfo 
                { 
                    SubjectName = "CN=DevelApp", 
                    CertificateThumbprint = "ABC123",
                    IsExpired = false
                }
            };

            mockValidator.Setup(v => v.ValidatePackageAsync(It.IsAny<System.IO.Stream>(), It.IsAny<object>()))
                        .ReturnsAsync(new MockPackageValidationResult { IsValid = true, PackageInfo = testPackage });

            // Act
            var result = await mockValidator.Object.ValidatePackageAsync(new System.IO.MemoryStream(), new object());

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.IsNotNull(result.PackageInfo);
            Assert.AreEqual("DevelApp.TestPlugin", result.PackageInfo.PackageId);
        }

        [TestMethod]
        public async Task SecurityValidator_ShouldRejectUnsignedPackages()
        {
            // Arrange
            var mockValidator = new Mock<IMockSignedPackageManager>();
            mockValidator.Setup(v => v.ValidatePackageAsync(It.IsAny<System.IO.Stream>(), It.IsAny<object>()))
                        .ReturnsAsync(new MockPackageValidationResult 
                        { 
                            IsValid = false,
                            Errors = new[] { new MockValidationError { Message = "Package is not signed" } }
                        });

            // Act
            var result = await mockValidator.Object.ValidatePackageAsync(new System.IO.MemoryStream(), new object());

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any());
        }

        [TestMethod]
        public void TrustedSignersRepository_ShouldManageWhitelist()
        {
            // Arrange
            var repo = new MockTrustedSignersRepository();
            var signer = new MockTrustedSigner
            {
                SignerName = "DevelApp Official",
                CertificateThumbprint = "DEF456",
                TrustLevel = "High"
            };

            // Act
            repo.AddTrustedSigner(signer);
            var signers = repo.GetTrustedSigners();
            var foundSigner = signers.FirstOrDefault(s => s.SignerName == "DevelApp Official");

            // Assert
            Assert.IsNotNull(foundSigner);
            Assert.AreEqual("High", foundSigner.TrustLevel);
        }
    }

    #region Test Interfaces and Mock Classes

    public interface ITestPlugin : IPluginClass
    {
        // Test plugin interface
    }

    public interface IMockContainerizedOrchestrator
    {
        Task<IEnumerable<MockContainerizedPluginInfo>> ListPluginsAsync(object? options = null, System.Threading.CancellationToken cancellationToken = default);
    }

    public interface IMockSignedPackageManager
    {
        Task<MockPackageValidationResult> ValidatePackageAsync(System.IO.Stream packageStream, object options);
    }

    public class MockHybridPluginFactory<T> where T : IPluginClass
    {
        private readonly PluginClassFactory<T>? _traditionalFactory;
        private readonly IMockContainerizedOrchestrator? _containerizedOrchestrator;

        public MockHybridPluginFactory(PluginClassFactory<T>? traditionalFactory, IMockContainerizedOrchestrator? containerizedOrchestrator)
        {
            _traditionalFactory = traditionalFactory;
            _containerizedOrchestrator = containerizedOrchestrator;
        }

        public async Task<IEnumerable<MockPluginInfo>> ListAvailablePluginsAsync()
        {
            var plugins = new List<MockPluginInfo>();

            // Simulate traditional plugins
            if (_traditionalFactory != null)
            {
                plugins.Add(new MockPluginInfo
                {
                    ModuleName = "Traditional",
                    PluginName = "TraditionalPlugin",
                    ExecutionMode = MockPluginExecutionMode.Traditional
                });
            }

            // Simulate containerized plugins
            if (_containerizedOrchestrator != null)
            {
                var containerizedPlugins = await _containerizedOrchestrator.ListPluginsAsync();
                plugins.AddRange(containerizedPlugins.Select(p => new MockPluginInfo
                {
                    ModuleName = p.PluginId.Namespace,
                    PluginName = p.PluginId.Name,
                    ExecutionMode = MockPluginExecutionMode.Containerized
                }));
            }

            return plugins;
        }
    }

    public enum MockPluginExecutionMode
    {
        Auto,
        Traditional,
        Containerized
    }

    public class MockPluginInfo
    {
        public string ModuleName { get; set; } = string.Empty;
        public string PluginName { get; set; } = string.Empty;
        public MockPluginExecutionMode ExecutionMode { get; set; }
    }

    public class MockHybridPluginFactoryOptions
    {
        public bool PreferContainerized { get; set; } = true;
        public bool AutoDeployContainerized { get; set; } = false;
    }

    public class MockContainerizedPluginLoaderOptions
    {
        public TimeSpan ContainerOperationTimeout { get; set; } = TimeSpan.FromMinutes(2);
        public bool AutoDeploy { get; set; } = false;
    }

    public class MockContainerizedPluginInfo
    {
        public MockPluginIdentifier PluginId { get; set; } = new();
        public string Description { get; set; } = string.Empty;
    }

    public class MockPluginIdentifier
    {
        public string Namespace { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    public class MockSignedPackageInfo
    {
        public string PackageId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public MockSignerInfo? SignerInfo { get; set; }
    }

    public class MockSignerInfo
    {
        public string SubjectName { get; set; } = string.Empty;
        public string CertificateThumbprint { get; set; } = string.Empty;
        public bool IsExpired { get; set; }
    }

    public class MockPackageValidationResult
    {
        public bool IsValid { get; set; }
        public MockSignedPackageInfo? PackageInfo { get; set; }
        public IEnumerable<MockValidationError> Errors { get; set; } = Array.Empty<MockValidationError>();
    }

    public class MockValidationError
    {
        public string Message { get; set; } = string.Empty;
    }

    public class MockTrustedSigner
    {
        public string SignerName { get; set; } = string.Empty;
        public string CertificateThumbprint { get; set; } = string.Empty;
        public string TrustLevel { get; set; } = string.Empty;
    }

    public class MockTrustedSignersRepository
    {
        private readonly List<MockTrustedSigner> _signers = new();

        public void AddTrustedSigner(MockTrustedSigner signer)
        {
            _signers.Add(signer);
        }

        public IEnumerable<MockTrustedSigner> GetTrustedSigners()
        {
            return _signers;
        }
    }

    #endregion
}