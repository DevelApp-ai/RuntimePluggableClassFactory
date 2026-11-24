using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Security
{
    /// <summary>
    /// Default implementation of plugin security validator
    /// Implements TDS requirement for comprehensive security validation.
    /// Path-based validation (ValidateAssemblyAsync) is METADATA-ONLY
    /// (PEReader + MetadataReader): it never loads the assembly into any
    /// AssemblyLoadContext, so validating a plugin does not prevent it from
    /// being unloaded later (issue #51; the former Assembly.LoadFrom-based
    /// validation pinned every validated plugin in the DEFAULT,
    /// non-collectible AssemblyLoadContext and made UnloadAllPlugins a no-op).
    /// </summary>
    public class DefaultPluginSecurityValidator : IPluginSecurityValidator
    {
        private readonly PluginSecuritySettings _settings;

        public DefaultPluginSecurityValidator(PluginSecuritySettings settings = null)
        {
            _settings = settings ?? new PluginSecuritySettings();
        }

        public async Task<PluginSecurityValidationResult> ValidateAssemblyAsync(string? assemblyPath)
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

                // Check assembly metadata WITHOUT loading the assembly.
                // Metadata-only inspection (PEReader + MetadataReader): validating a
                // plugin assembly must never load it into an AssemblyLoadContext.
                // Loading here previously used Assembly.LoadFrom, which pinned every
                // validated plugin assembly in the DEFAULT, non-collectible
                // AssemblyLoadContext; FilePluginLoader then reused that pinned copy
                // instead of its collectible PluginLoadContext copy, making
                // UnloadAllPlugins a no-op for in-use assemblies (issue #51).
                try
                {
                    using (var fileStream = File.OpenRead(assemblyPath))
                    using (var peReader = new PEReader(fileStream))
                    {
                        if (!peReader.HasMetadata)
                        {
                            result.Issues.Add(new SecurityIssue
                            {
                                Code = "SEC005",
                                Description = "Failed to read assembly for validation: file has no managed metadata",
                                Severity = SecurityRiskLevel.High,
                                Location = assemblyPath
                            });
                        }
                        else
                        {
                            var metadataReader = peReader.GetMetadataReader();
                            var metadataResult = ValidateAssemblyMetadata(metadataReader, assemblyPath);
                            result.Issues.AddRange(metadataResult.Issues);
                            result.Warnings.AddRange(metadataResult.Warnings);

                            if (metadataResult.RiskLevel > result.RiskLevel)
                            {
                                result.RiskLevel = metadataResult.RiskLevel;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Issues.Add(new SecurityIssue
                    {
                        Code = "SEC005",
                        Description = $"Failed to read assembly for validation: {ex.Message}",
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

        public PluginSecurityValidationResult ValidateLoadedAssembly(Assembly? assembly)
        {
            var result = new PluginSecurityValidationResult();

            try
            {
                // Check assembly location
                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    var assemblyDir = Path.GetDirectoryName(assembly.Location);
                    if (assemblyDir != null && !_settings.TrustedPaths.Any(tp => assemblyDir.StartsWith(tp, StringComparison.OrdinalIgnoreCase)))
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

        public PluginSecurityValidationResult ValidatePluginTypes(IEnumerable<Type>? pluginTypes)
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
                            Location = type.FullName ?? "Unknown"
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
                            Location = type.FullName ?? "Unknown"
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
                        Location = type.FullName ?? "Unknown"
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

        /// <summary>
        /// Method-name heuristics shared by the reflection path (ValidateMethod) and
        /// the metadata-only path (ValidateAssemblyMetadata), so both paths apply
        /// the same dangerous-pattern list.
        /// </summary>
        private static readonly string[] DangerousMethodPatterns =
        {
            "process.start", "file.delete", "directory.delete", "registry",
            "reflection.emit", "assembly.load", "appdomain", "marshal"
        };

        /// <summary>
        /// Metadata-only assembly validation (issue #51): applies the SAME checks
        /// as ValidateLoadedAssembly - trusted path (SEC101), prohibited base types
        /// (SEC201), prohibited namespaces (SEC202), dangerous method-name
        /// heuristics (SEC401), prohibited field types (SEC501) and security-related
        /// assembly attributes (SEC301) - over PEReader/MetadataReader WITHOUT
        /// loading the assembly into any AssemblyLoadContext (no pinning, unloadable).
        /// Type-identity checks (IsSubclassOf / IsAssignableFrom / field-type
        /// identity) become full-name comparisons over the metadata, which is an
        /// equivalent match for the policy lists these settings carry.
        /// </summary>
        private PluginSecurityValidationResult ValidateAssemblyMetadata(MetadataReader reader, string assemblyPath)
        {
            var result = new PluginSecurityValidationResult();

            // SEC101: trusted path (same check as ValidateLoadedAssembly, taken
            // from the file path instead of the loaded assembly's Location)
            var assemblyDir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
            if (assemblyDir != null && !_settings.TrustedPaths.Any(tp => assemblyDir.StartsWith(tp, StringComparison.OrdinalIgnoreCase)))
            {
                result.Warnings.Add(new SecurityWarning
                {
                    Code = "SEC101",
                    Description = "Assembly is not loaded from a trusted path",
                    Location = assemblyPath
                });
                result.RiskLevel = SecurityRiskLevel.Medium;
            }

            var prohibitedBaseTypeNames = GetFullNames(_settings.ProhibitedBaseTypes);
            var prohibitedFieldTypeNames = GetFullNames(_settings.ProhibitedFieldTypes);

            foreach (var typeDefinitionHandle in reader.TypeDefinitions)
            {
                var typeDefinition = reader.GetTypeDefinition(typeDefinitionHandle);
                var namespaceName = typeDefinition.Namespace.IsNil
                    ? null
                    : reader.GetString(typeDefinition.Namespace);
                var typeName = reader.GetString(typeDefinition.Name);
                var fullName = string.IsNullOrEmpty(namespaceName)
                    ? typeName
                    : $"{namespaceName}.{typeName}";

                // SEC202: prohibited namespaces (same StartsWith semantics as the reflection path)
                if (!string.IsNullOrEmpty(namespaceName) &&
                    _settings.ProhibitedNamespaces.Any(pns => namespaceName.StartsWith(pns, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Issues.Add(new SecurityIssue
                    {
                        Code = "SEC202",
                        Description = $"Type is in prohibited namespace: {namespaceName}",
                        Severity = SecurityRiskLevel.High,
                        Location = fullName
                    });
                }

                // SEC201: prohibited base types and interfaces (by full name)
                if (prohibitedBaseTypeNames.Count > 0)
                {
                    string? prohibitedBase = FindProhibitedBaseTypeName(reader, typeDefinition, prohibitedBaseTypeNames);
                    if (prohibitedBase != null)
                    {
                        result.Issues.Add(new SecurityIssue
                        {
                            Code = "SEC201",
                            Description = $"Type inherits from prohibited base type: {prohibitedBase}",
                            Severity = SecurityRiskLevel.High,
                            Location = fullName
                        });
                    }
                }

                // SEC401: dangerous method-name heuristics (declared methods)
                foreach (var methodDefinitionHandle in typeDefinition.GetMethods())
                {
                    var methodDefinition = reader.GetMethodDefinition(methodDefinitionHandle);
                    var methodName = reader.GetString(methodDefinition.Name).ToLowerInvariant();
                    var declaringTypeName = fullName.ToLowerInvariant();

                    foreach (var pattern in DangerousMethodPatterns)
                    {
                        if (methodName.Contains(pattern) || declaringTypeName.Contains(pattern))
                        {
                            result.Warnings.Add(new SecurityWarning
                            {
                                Code = "SEC401",
                                Description = $"Method may perform potentially dangerous operations: {pattern}",
                                Location = $"{fullName}.{reader.GetString(methodDefinition.Name)}"
                            });
                            break;
                        }
                    }
                }

                // SEC501: prohibited field types (by full name)
                if (prohibitedFieldTypeNames.Count > 0)
                {
                    foreach (var fieldDefinitionHandle in typeDefinition.GetFields())
                    {
                        var fieldDefinition = reader.GetFieldDefinition(fieldDefinitionHandle);
                        string fieldTypeName;
                        try
                        {
                            fieldTypeName = fieldDefinition.DecodeSignature(MetadataTypeNameProvider.Instance, null);
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add(new SecurityWarning
                            {
                                Code = "SEC502",
                                Description = $"Error validating field: {ex.Message}",
                                Location = $"{fullName}.{reader.GetString(fieldDefinition.Name)}"
                            });
                            continue;
                        }

                        if (prohibitedFieldTypeNames.Contains(fieldTypeName))
                        {
                            result.Issues.Add(new SecurityIssue
                            {
                                Code = "SEC501",
                                Description = $"Field has prohibited type: {fieldTypeName}",
                                Severity = SecurityRiskLevel.Medium,
                                Location = $"{fullName}.{reader.GetString(fieldDefinition.Name)}"
                            });
                        }
                    }
                }
            }

            // SEC301: security-related assembly-level attributes
            try
            {
                foreach (var customAttributeHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
                {
                    var attributeName = GetCustomAttributeTypeName(reader, customAttributeHandle);
                    if (attributeName == null)
                    {
                        continue;
                    }

                    if (attributeName.Contains("Security") || attributeName.Contains("Permission"))
                    {
                        result.Warnings.Add(new SecurityWarning
                        {
                            Code = "SEC301",
                            Description = $"Assembly contains security-related attribute: {attributeName}",
                            Location = assemblyPath
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
                    Location = assemblyPath
                });
            }

            result.IsValid = !result.Issues.Any(i => i.Severity >= SecurityRiskLevel.High);
            return result;
        }

        /// <summary>
        /// Walks a type definition's base-type chain and interface list (metadata
        /// only) and returns the full name of the first prohibited base/interface
        /// type found, or null.
        /// </summary>
        private static string? FindProhibitedBaseTypeName(MetadataReader reader, TypeDefinition typeDefinition, HashSet<string> prohibitedNames)
        {
            foreach (var interfaceImplementationHandle in typeDefinition.GetInterfaceImplementations())
            {
                var interfaceImplementation = reader.GetInterfaceImplementation(interfaceImplementationHandle);
                var interfaceName = GetEntityTypeName(reader, interfaceImplementation.Interface);
                if (interfaceName != null && prohibitedNames.Contains(interfaceName))
                {
                    return interfaceName;
                }
            }

            // Base-type chain: walk in-module bases; external bases cannot be
            // walked further (their own bases are defined in other assemblies), but
            // a prohibited base is itself matched at the first external level.
            var current = typeDefinition.BaseType;
            var depthGuard = 0;
            while (!current.IsNil && depthGuard++ < 64)
            {
                if (current.Kind == HandleKind.TypeDefinition)
                {
                    var baseDefinitionHandle = (TypeDefinitionHandle)current;
                    var baseDefinition = reader.GetTypeDefinition(baseDefinitionHandle);
                    var baseName = GetTypeDefinitionFullName(reader, baseDefinition);
                    if (prohibitedNames.Contains(baseName))
                    {
                        return baseName;
                    }
                    current = baseDefinition.BaseType;
                }
                else
                {
                    var externalName = GetEntityTypeName(reader, current);
                    if (externalName != null && prohibitedNames.Contains(externalName))
                    {
                        return externalName;
                    }
                    return null;
                }
            }

            return null;
        }

        private static string GetTypeDefinitionFullName(MetadataReader reader, TypeDefinition typeDefinition)
        {
            var namespaceName = typeDefinition.Namespace.IsNil
                ? null
                : reader.GetString(typeDefinition.Namespace);
            var name = reader.GetString(typeDefinition.Name);
            return string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
        }

        private static string? GetEntityTypeName(MetadataReader reader, EntityHandle entityHandle)
        {
            if (entityHandle.Kind == HandleKind.TypeReference)
            {
                var typeReference = reader.GetTypeReference((TypeReferenceHandle)entityHandle);
                var namespaceName = typeReference.Namespace.IsNil
                    ? null
                    : reader.GetString(typeReference.Namespace);
                var name = reader.GetString(typeReference.Name);
                return string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
            }
            if (entityHandle.Kind == HandleKind.TypeDefinition)
            {
                return GetTypeDefinitionFullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)entityHandle));
            }
            if (entityHandle.Kind == HandleKind.TypeSpecification)
            {
                var typeSpecification = reader.GetTypeSpecification((TypeSpecificationHandle)entityHandle);
                try
                {
                    return typeSpecification.DecodeSignature(MetadataTypeNameProvider.Instance, null);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static string? GetCustomAttributeTypeName(MetadataReader reader, CustomAttributeHandle customAttributeHandle)
        {
            try
            {
                var customAttribute = reader.GetCustomAttribute(customAttributeHandle);
                if (customAttribute.Constructor.Kind == HandleKind.MemberReference)
                {
                    var constructor = reader.GetMemberReference((MemberReferenceHandle)customAttribute.Constructor);
                    if (constructor.Parent.Kind == HandleKind.TypeReference)
                    {
                        var typeReference = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
                        var namespaceName = typeReference.Namespace.IsNil
                            ? null
                            : reader.GetString(typeReference.Namespace);
                        var name = reader.GetString(typeReference.Name);
                        return string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
                    }
                    if (constructor.Parent.Kind == HandleKind.TypeDefinition)
                    {
                        return GetTypeDefinitionFullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)constructor.Parent));
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static HashSet<string> GetFullNames(IEnumerable<Type>? types)
        {
            var fullNames = new HashSet<string>(StringComparer.Ordinal);
            if (types != null)
            {
                foreach (var type in types)
                {
                    if (type != null && !string.IsNullOrEmpty(type.FullName))
                    {
                        fullNames.Add(type.FullName);
                    }
                }
            }

            return fullNames;
        }

        /// <summary>
        /// Minimal signature type provider that decodes metadata type references
        /// into their full names for the metadata-only validation path. Generic
        /// instantiations resolve to the generic type's own name (System.Collections.Generic.List`1),
        /// which is sufficient for the prohibited-type name matching.
        /// </summary>
        private sealed class MetadataTypeNameProvider : ISignatureTypeProvider<string, object?>
        {
            internal static readonly MetadataTypeNameProvider Instance = new();

            private MetadataTypeNameProvider()
            {
            }

            public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
            public string GetByReferenceType(string elementType) => $"{elementType}&";
            public string GetFunctionPointerType(MethodSignature<string> signature) => "IntPtr";
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;
            public string GetGenericMethodParameter(object? genericContext, int index) => $"!(GV:{index})";
            public string GetGenericTypeParameter(object? genericContext, int index) => $"!(GP:{index})";
            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
            public string GetPinnedType(string elementType) => elementType;
            public string GetPointerType(string elementType) => $"{elementType}*";
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => $"System.{typeCode}";
            public string GetSZArrayType(string elementType) => $"{elementType}[]";

            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
                => GetTypeDefinitionFullName(reader, reader.GetTypeDefinition(handle));

            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                var typeReference = reader.GetTypeReference(handle);
                var namespaceName = typeReference.Namespace.IsNil
                    ? null
                    : reader.GetString(typeReference.Namespace);
                var name = reader.GetString(typeReference.Name);
                return string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
            }

            public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
                => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
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

                // Flag potentially dangerous methods (shared pattern list,
                // also used by the metadata-only validation path)
                foreach (var pattern in DangerousMethodPatterns)
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

