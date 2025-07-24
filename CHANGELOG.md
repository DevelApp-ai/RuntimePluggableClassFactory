# Changelog

All notable changes to the RuntimePluggableClassFactory project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2025-07-24

### Added - TDS Implementation Complete

#### Dynamic Plugin Loading/Unloading
- **PluginLoadContext**: Collectible AssemblyLoadContext for proper memory cleanup
- **FilePluginLoader**: Enhanced with `UnloadPlugin()` and `UnloadAllPlugins()` methods
- **PluginWatcher**: Runtime plugin detection and hot-swapping capabilities
- Proper resource disposal and memory management

#### Enhanced Stability & Error Handling
- **PluginExecutionSandbox**: Isolated execution environment for plugins
- **Enhanced PluginClassFactory**: Comprehensive error handling with events
- **Error Events**: PluginInstantiationError, PluginLoadingFailed, SecurityValidationFailed
- Graceful degradation on plugin failures
- Detailed error reporting and logging

#### Type Safety
- **ITypedPluginClass<TInput, TOutput>**: Generic interface for type-safe plugin execution
- **PluginExecutionContext**: Functional context with logging, cancellation, and properties
- **TypedPluginClassFactory**: Type-safe plugin factory implementation
- **PluginExecutionResult<T>**: Strongly-typed execution results
- Async execution with timeout support

#### Security Hardening
- **IPluginSecurityValidator**: Security validation interface
- **DefaultPluginSecurityValidator**: Comprehensive security validation implementation
- **PluginSecuritySettings**: Configurable security policies (Default, Strict, Permissive)
- Multi-level security validation (assembly, type, method)
- Digital signature verification and trusted path validation
- Prohibited namespace and type detection

#### Comprehensive Testing
- **48 tests** across 7 categories:
  - Unit Tests (8) - Core functionality
  - Stability Tests (3) - Error handling and resilience
  - Unloading Tests (2) - Dynamic plugin unloading
  - Typed Plugin Tests (6) - Type-safe plugin system
  - Security Tests (13) - Security hardening validation
  - Integration Tests (8) - End-to-end workflows
  - Performance Tests (8) - Performance and scalability
- **Performance benchmarks** with automated validation
- **TestingStrategy.md**: Comprehensive testing documentation

#### Documentation
- **TDS_IMPLEMENTATION.md**: Complete implementation documentation
- **TestingStrategy.md**: Comprehensive testing approach
- **README.md**: Updated with new features and usage examples
- Migration guide for existing implementations
- Architecture overview and performance characteristics

### Changed

#### Breaking Changes
1. **Assembly Loading**: Direct `Assembly.LoadFrom()` replaced with `AssemblyLoadContext`
2. **Plugin Discovery**: Plugin enumeration now async (`ListAllPossiblePluginsAsync()`)
3. **Error Handling**: Exceptions now wrapped in events for better error management
4. **Security**: Plugin loading now includes security validation by default
5. **Target Framework**: Upgraded from .NET Standard 2.0 to .NET 8.0

#### Performance Improvements
- Plugin discovery: < 5 seconds
- Plugin instantiation: < 100ms average, < 500ms max
- Plugin execution: < 10ms average for simple operations
- Concurrent throughput: > 100 executions/second
- Security validation: < 500ms average
- Memory growth: < 50MB under load
- Load/unload cycles: < 2 seconds average

### Migration Guide

#### From v1.x to v2.0

1. **Plugin Interfaces**: Existing `IPluginClass` implementations remain compatible
2. **Plugin Loading**: Replace direct assembly loading with `FilePluginLoader<T>`
3. **Error Handling**: Subscribe to error events for comprehensive error management
4. **Security**: Add security validation to plugin loading process
5. **Type Safety**: Optionally migrate to `ITypedPluginClass<TInput, TOutput>` for type safety

#### Example Migration

**Before (v1.x):**
```csharp
var pluginLoader = new FilePluginLoader<IMyPlugin>(pluginDirectory);
var factory = new PluginClassFactory<IMyPlugin>(pluginLoader);
```

**After (v2.0):**
```csharp
var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
var pluginLoader = new FilePluginLoader<IMyPlugin>(pluginDirectory, securityValidator);
var factory = new PluginClassFactory<IMyPlugin>(pluginLoader);

// Subscribe to error events
factory.PluginInstantiationError += (sender, e) => Console.WriteLine($"Plugin error: {e.Exception.Message}");

// Use type-safe factory (optional)
var typedFactory = new TypedPluginClassFactory<IMyTypedPlugin, MyInput, MyOutput>(pluginLoader);
```

## [1.0.7] - 2020-11-07

### Added
- Initial release of RuntimePluggableClassFactory
- Basic plugin loading from file directories
- Version-aware plugin management
- Interface-based plugin architecture
- Thread-safe plugin execution
- Basic error handling

### Features
- Dynamic plugin loading from file directories
- Version management and plugin discovery
- `IPluginClass` interface for plugin development
- `PluginClassFactory<T>` for plugin instantiation
- `FilePluginLoader<T>` for file-based plugin loading
- Basic assembly loading and type resolution

---

## Version Numbering

- **Major version** (X.0.0): Breaking changes, significant architectural changes
- **Minor version** (X.Y.0): New features, backward compatible
- **Patch version** (X.Y.Z): Bug fixes, minor improvements

## Support

For questions, issues, or support:
- Review the comprehensive test suite (48 tests)
- Check the [TDS Implementation Guide](TDS_IMPLEMENTATION.md)
- Examine the [Testing Strategy](TestingStrategy.md)
- Create an issue on [GitHub](https://github.com/DevelApp-dk/RuntimePluggableClassFactory/issues)

