using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.FilePlugin;
using DevelApp.RuntimePluggableClassFactory.Security;
using PluginImplementations;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace RuntimePluggableClassFactory.Test
{
    public class SecurityTests
    {
        [Fact]
        public void Test_PluginSecuritySettings_Default()
        {
            var settings = PluginSecuritySettings.CreateDefault();
            
            Assert.False(settings.RequireDigitalSignature);
            Assert.Equal(50 * 1024 * 1024, settings.MaxAssemblySizeBytes);
            Assert.Contains(".dll", settings.AllowedExtensions);
            Assert.Contains(".exe", settings.AllowedExtensions);
            Assert.True(settings.AllowReflection);
            Assert.False(settings.AllowFileSystemAccess);
            Assert.False(settings.AllowNetworkAccess);
            Assert.True(settings.ValidateSettings());
        }

        [Fact]
        public void Test_PluginSecuritySettings_Strict()
        {
            var settings = PluginSecuritySettings.CreateStrict();
            
            Assert.True(settings.RequireDigitalSignature);
            Assert.Equal(10 * 1024 * 1024, settings.MaxAssemblySizeBytes);
            Assert.False(settings.AllowReflection);
            Assert.False(settings.AllowFileSystemAccess);
            Assert.False(settings.AllowNetworkAccess);
            Assert.Contains("System.IO", settings.ProhibitedNamespaces);
            Assert.True(settings.ValidateSettings());
        }

        [Fact]
        public void Test_PluginSecuritySettings_Permissive()
        {
            var settings = PluginSecuritySettings.CreatePermissive();
            
            Assert.False(settings.RequireDigitalSignature);
            Assert.Equal(200 * 1024 * 1024, settings.MaxAssemblySizeBytes);
            Assert.True(settings.AllowReflection);
            Assert.True(settings.AllowFileSystemAccess);
            Assert.True(settings.AllowNetworkAccess);
            Assert.Empty(settings.ProhibitedNamespaces);
            Assert.True(settings.ValidateSettings());
        }

        [Fact]
        public void Test_PluginSecuritySettings_TrustedPaths()
        {
            var settings = new PluginSecuritySettings();
            var currentDir = Directory.GetCurrentDirectory();
            
            settings.AddTrustedPath(currentDir);
            Assert.Contains(currentDir, settings.TrustedPaths);
            
            settings.RemoveTrustedPath(currentDir);
            Assert.DoesNotContain(currentDir, settings.TrustedPaths);
        }

        [Fact]
        public async Task Test_DefaultPluginSecurityValidator_ValidAssembly()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            
            var pluginDll = Directory.GetFiles(pathStringExpanded, "*.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (pluginDll != null)
            {
                var validator = new DefaultPluginSecurityValidator();
                var result = await validator.ValidateAssemblyAsync(pluginDll);
                
                // The result should be valid (though may have warnings)
                Assert.True(result.IsValid || result.RiskLevel <= SecurityRiskLevel.Medium);
                Assert.NotNull(result.Issues);
                Assert.NotNull(result.Warnings);
            }
        }

        [Fact]
        public async Task Test_DefaultPluginSecurityValidator_NonExistentFile()
        {
            var validator = new DefaultPluginSecurityValidator();
            var result = await validator.ValidateAssemblyAsync("nonexistent.dll");
            
            Assert.False(result.IsValid);
            Assert.Equal(SecurityRiskLevel.Critical, result.RiskLevel);
            Assert.Contains(result.Issues, i => i.Code == "SEC001");
        }

        [Fact]
        public async Task Test_DefaultPluginSecurityValidator_InvalidExtension()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var validator = new DefaultPluginSecurityValidator();
                var result = await validator.ValidateAssemblyAsync(tempFile);
                
                Assert.False(result.IsValid);
                Assert.Contains(result.Issues, i => i.Code == "SEC002");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task Test_DefaultPluginSecurityValidator_FileSizeLimit()
        {
            var settings = new PluginSecuritySettings
            {
                MaxAssemblySizeBytes = 1 // Very small limit
            };
            
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            
            var pluginDll = Directory.GetFiles(pathStringExpanded, "*.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (pluginDll != null)
            {
                var validator = new DefaultPluginSecurityValidator(settings);
                var result = await validator.ValidateAssemblyAsync(pluginDll);
                
                Assert.Contains(result.Issues, i => i.Code == "SEC003");
            }
        }

        [Fact]
        public void Test_FilePluginLoader_WithSecurityValidator()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            var validator = new DefaultPluginSecurityValidator();
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(pluginDirectory, validator);
            
            filePluginLoader.SecurityValidationFailed += (sender, args) =>
            {
                Assert.NotNull(args.ValidationResult);
                Assert.NotNull(args.FileName);
                Assert.NotNull(args.PluginPath);
            };
            
            // This should work without throwing exceptions
            var possiblePlugins = filePluginLoader.ListAllPossiblePluginsAsync().Result;
            Assert.NotNull(possiblePlugins);
            
            // Security event might fire for warnings, which is expected
        }

        [Fact]
        public async Task Test_SecurityValidator_WithStrictSettings()
        {
            string pathString = ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "PluginFolder";
            string assemblyPath = GetType().Assembly.Location;
            string pathStringExpanded = Path.GetFullPath(pathString, assemblyPath);
            Uri pluginDirectory = new Uri(pathStringExpanded);
            
            var strictSettings = PluginSecuritySettings.CreateStrict();
            var validator = new DefaultPluginSecurityValidator(strictSettings);
            var filePluginLoader = new FilePluginLoader<ISpecificInterface>(pluginDirectory, validator);
            
            filePluginLoader.SecurityValidationFailed += (sender, args) =>
            {
                // With strict settings, we expect security issues
                Assert.True(args.ValidationResult.Issues.Any() || args.ValidationResult.Warnings.Any());
            };
            
            var possiblePlugins = await filePluginLoader.ListAllPossiblePluginsAsync();
            Assert.NotNull(possiblePlugins);
            
            // With strict settings, security events are likely to fire
        }

        [Fact]
        public void Test_SecurityValidationResult_CreateMethods()
        {
            var validResult = PluginSecurityValidationResult.CreateValid();
            Assert.True(validResult.IsValid);
            Assert.Equal(SecurityRiskLevel.Low, validResult.RiskLevel);
            Assert.Empty(validResult.Issues);
            
            var issues = new[]
            {
                new SecurityIssue
                {
                    Code = "TEST001",
                    Description = "Test issue",
                    Severity = SecurityRiskLevel.High
                }
            };
            
            var invalidResult = PluginSecurityValidationResult.CreateInvalid(issues);
            Assert.False(invalidResult.IsValid);
            Assert.Equal(SecurityRiskLevel.High, invalidResult.RiskLevel);
            Assert.Single(invalidResult.Issues);
        }

        [Fact]
        public void Test_SecurityIssue_Properties()
        {
            var issue = new SecurityIssue
            {
                Code = "SEC001",
                Description = "Test security issue",
                Severity = SecurityRiskLevel.Medium,
                Location = "TestLocation"
            };
            
            Assert.Equal("SEC001", issue.Code);
            Assert.Equal("Test security issue", issue.Description);
            Assert.Equal(SecurityRiskLevel.Medium, issue.Severity);
            Assert.Equal("TestLocation", issue.Location);
            Assert.NotNull(issue.Details);
        }

        [Fact]
        public void Test_SecurityWarning_Properties()
        {
            var warning = new SecurityWarning
            {
                Code = "SEC101",
                Description = "Test security warning",
                Location = "TestLocation"
            };
            
            Assert.Equal("SEC101", warning.Code);
            Assert.Equal("Test security warning", warning.Description);
            Assert.Equal("TestLocation", warning.Location);
            Assert.NotNull(warning.Details);
        }
    }
}

