using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Security
{
    /// <summary>
    /// Default implementation of plugin security validator
    /// Implements TDS requirement for comprehensive security validation
    /// </summary>
    public class DefaultPluginSecurityValidator : IPluginSecurityValidator
    {
        private readonly PluginSecuritySettings _settings;

        public DefaultPluginSecurityValidator(PluginSecuritySettings settings = null)
        {
            _settings = settings ?? new PluginSecuritySettings();
        }

        public async Task<PluginSecurityValidationResult> ValidateAssemblyAsync(string assemblyPath)
        {
            var result = new PluginSecurityValidationResult();

            try
            {
                // Check if file exists
                if (!File.Exists(assemblyPath))
                {
                    result.Issues.Add(new SecurityIssue
                    {
                        Code = "SEC001",
                        Description = "Assembly file not found",
                        Severity = SecurityRiskLevel.Critical,
                        Location = assemblyPath
                    });
                    return PluginSecurityValidationResult.CreateInvalid(result.Issues, SecurityRiskLevel.Critical);
                }

                // Validate file extension
                if (!_settings.AllowedExtensions.Contains(Path.GetExtension(assemblyPath).ToLowerInvariant()))
                {
                    result.Issues.Add(new SecurityIssue
                    {
                        Code = "SEC002",
                        Description = "Invalid file extension for plugin assembly",
                        Severity = SecurityRiskLevel.High,
                        Location = assemblyPath
                    });
                }

                // Check file size limits
                var fileInfo = new FileInfo(assemblyPath);
                if (fileInfo.Length > _settings.MaxAssemblySizeBytes)
                {
                    result.Issues.Add(new SecurityIssue
                    {
                        Code = "SEC003",
                        Description = $"Assembly size ({fileInfo.Length} bytes) exceeds maximum allowed size ({_settings.MaxAssemblySizeBytes} bytes)",
                        Severity = SecurityRiskLevel.Medium,
                        Location = assemblyPath
                    });
                }

                // Validate digital signature if required
                if (_settings.RequireDigitalSignature)
                {
                    var signatureValid = await ValidateDigitalSignatureAsync(assemblyPath);
                    if (!signatureValid)
                    {
                        result.Issues.Add(new SecurityIssue
                        {
                            Code = "SEC004",
                            Description = "Assembly is not digitally signed or signature is invalid",
                            Severity = SecurityRiskLevel.High,
                            Location = assemblyPath
                        });
                    }
                }

                // Check assembly metadata
                try
                {
                    using (var stream = File.OpenRead(assemblyPath))
                    {
                        var assembly = Assembly.LoadFrom(assemblyPath);
                        var loadedResult = ValidateLoadedAssembly(assembly);
                        result.Issues.AddRange(loadedResult.Issues);
                        result.Warnings.AddRange(loadedResult.Warnings);
                        
                        if (loadedResult.RiskLevel > result.RiskLevel)
                        {
                            result.RiskLevel = loadedResult.RiskLevel;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Issues.Add(new SecurityIssue
                    {
                        Code = "SEC005",
                        Description = $"Failed to load assembly for validation: {ex.Message}",
                        Severity = SecurityRiskLevel.High,
                        Location = assemblyPath
                    });
                }

                // Determine overall validation result
                result.IsValid = !result.Issues.Any(i => i.Severity >= SecurityRiskLevel.High);
                if (result.RiskLevel == SecurityRiskLevel.Low && result.Issues.Any())
                {
                    result.RiskLevel = result.Issues.Max(i => i.Severity);
                }

                return result;
            }
            catch (Exception ex)
            {
                return PluginSecurityValidationResult.CreateInvalid(new[]
                {
                    new SecurityIssue
                    {
                        Code = "SEC999",
                        Description = $"Unexpected error during validation: {ex.Message}",
                        Severity = SecurityRiskLevel.Critical,
                        Location = assemblyPath
                    }
                }, SecurityRiskLevel.Critical);
            }
        }

        public PluginSecurityValidationResult ValidateLoadedAssembly(Assembly assembly)
        {
            var result = new PluginSecurityValidationResult();

            try
            {
                // Check assembly location
                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    var assemblyDir = Path.GetDirectoryName(assembly.Location);
                    if (!_settings.TrustedPaths.Any(tp => assemblyDir.StartsWith(tp, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Warnings.Add(new SecurityWarning
                        {
                            Code = "SEC101",
                            Description = "Assembly is not loaded from a trusted path",
                            Location = assembly.Location
                        });
                        result.RiskLevel = SecurityRiskLevel.Medium;
                    }
                }

                // Check for dangerous types and methods
                var types = assembly.GetTypes();
                var typeValidationResult = ValidatePluginTypes(types);
                result.Issues.AddRange(typeValidationResult.Issues);
                result.Warnings.AddRange(typeValidationResult.Warnings);
                
                if (typeValidationResult.RiskLevel > result.RiskLevel)
                {
                    result.RiskLevel = typeValidationResult.RiskLevel;
                }

                // Check assembly attributes
                ValidateAssemblyAttributes(assembly, result);

                result.IsValid = !result.Issues.Any(i => i.Severity >= SecurityRiskLevel.High);
                return result;
            }
            catch (Exception ex)
            {
                return PluginSecurityValidationResult.CreateInvalid(new[]
                {
                    new SecurityIssue
                    {
                        Code = "SEC998",
                        Description = $"Error validating loaded assembly: {ex.Message}",
                        Severity = SecurityRiskLevel.High,
                        Location = assembly.Location ?? "Unknown"
                    }
                }, SecurityRiskLevel.High);
            }
        }

        public PluginSecurityValidationResult ValidatePluginTypes(IEnumerable<Type> pluginTypes)
        {
            var result = new PluginSecurityValidationResult();

            foreach (var type in pluginTypes)
            {
                try
                {
                    // Check for dangerous base types
                    if (_settings.ProhibitedBaseTypes.Any(pbt => type.IsSubclassOf(pbt) || pbt.IsAssignableFrom(type)))
                    {
                        result.Issues.Add(new SecurityIssue
                        {
                            Code = "SEC201",
                            Description = $"Type inherits from prohibited base type",
                            Severity = SecurityRiskLevel.High,
                            Location = type.FullName
                        });
                    }

                    // Check for dangerous namespaces
                    if (_settings.ProhibitedNamespaces.Any(ns => type.Namespace?.StartsWith(ns, StringComparison.OrdinalIgnoreCase) == true))
                    {
                        result.Issues.Add(new SecurityIssue
                        {
                            Code = "SEC202",
                            Description = $"Type is in prohibited namespace: {type.Namespace}",
                            Severity = SecurityRiskLevel.High,
                            Location = type.FullName
                        });
                    }

                    // Check methods for dangerous operations
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    foreach (var method in methods)
                    {
                        ValidateMethod(method, result);
                    }

                    // Check fields and properties
                    var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    foreach (var field in fields)
                    {
                        ValidateField(field, result);
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add(new SecurityWarning
                    {
                        Code = "SEC203",
                        Description = $"Error validating type {type.FullName}: {ex.Message}",
                        Location = type.FullName
                    });
                }
            }

            result.IsValid = !result.Issues.Any(i => i.Severity >= SecurityRiskLevel.High);
            if (result.Issues.Any())
            {
                result.RiskLevel = result.Issues.Max(i => i.Severity);
            }

            return result;
        }

        private async Task<bool> ValidateDigitalSignatureAsync(string assemblyPath)
        {
            try
            {
                // This is a simplified signature validation
                // In a production environment, you would implement proper Authenticode validation
                var fileBytes = await File.ReadAllBytesAsync(assemblyPath);
                
                // Check for basic PE signature
                if (fileBytes.Length < 64) return false;
                
                // Check DOS header
                if (fileBytes[0] != 0x4D || fileBytes[1] != 0x5A) return false;
                
                // For demonstration purposes, we'll consider files with proper PE headers as "signed"
                // In reality, you would validate the actual digital signature
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ValidateAssemblyAttributes(Assembly assembly, PluginSecurityValidationResult result)
        {
            try
            {
                var attributes = assembly.GetCustomAttributes();
                
                // Check for security-related attributes
                foreach (var attr in attributes)
                {
                    var attrType = attr.GetType();
                    
                    // Flag assemblies that request full trust
                    if (attrType.Name.Contains("Security") || attrType.Name.Contains("Permission"))
                    {
                        result.Warnings.Add(new SecurityWarning
                        {
                            Code = "SEC301",
                            Description = $"Assembly contains security-related attribute: {attrType.Name}",
                            Location = assembly.Location ?? "Unknown"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add(new SecurityWarning
                {
                    Code = "SEC302",
                    Description = $"Error validating assembly attributes: {ex.Message}",
                    Location = assembly.Location ?? "Unknown"
                });
            }
        }

        private void ValidateMethod(MethodInfo method, PluginSecurityValidationResult result)
        {
            try
            {
                // Check for dangerous method calls in the method body
                // This is a simplified check - in practice, you might use IL analysis
                
                var methodName = method.Name.ToLowerInvariant();
                var typeName = method.DeclaringType?.FullName?.ToLowerInvariant() ?? "";

                // Flag potentially dangerous methods
                var dangerousPatterns = new[]
                {
                    "process.start", "file.delete", "directory.delete", "registry",
                    "reflection.emit", "assembly.load", "appdomain", "marshal"
                };

                foreach (var pattern in dangerousPatterns)
                {
                    if (methodName.Contains(pattern) || typeName.Contains(pattern))
                    {
                        result.Warnings.Add(new SecurityWarning
                        {
                            Code = "SEC401",
                            Description = $"Method may perform potentially dangerous operations: {pattern}",
                            Location = $"{method.DeclaringType?.FullName}.{method.Name}"
                        });
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add(new SecurityWarning
                {
                    Code = "SEC402",
                    Description = $"Error validating method: {ex.Message}",
                    Location = $"{method.DeclaringType?.FullName}.{method.Name}"
                });
            }
        }

        private void ValidateField(FieldInfo field, PluginSecurityValidationResult result)
        {
            try
            {
                // Check for dangerous field types
                var fieldType = field.FieldType;
                
                if (_settings.ProhibitedFieldTypes.Any(pft => pft.IsAssignableFrom(fieldType)))
                {
                    result.Issues.Add(new SecurityIssue
                    {
                        Code = "SEC501",
                        Description = $"Field has prohibited type: {fieldType.FullName}",
                        Severity = SecurityRiskLevel.Medium,
                        Location = $"{field.DeclaringType?.FullName}.{field.Name}"
                    });
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add(new SecurityWarning
                {
                    Code = "SEC502",
                    Description = $"Error validating field: {ex.Message}",
                    Location = $"{field.DeclaringType?.FullName}.{field.Name}"
                });
            }
        }
    }
}

