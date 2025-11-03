# RuntimePluggableClassFactory

A comprehensive .NET library for dynamic plugin loading, execution, and management with enhanced stability, type safety, and security features.

## 🚀 TDS Implementation Complete

This project has been enhanced with a complete Technical Design Specification (TDS) implementation featuring:

- ✅ **Dynamic Plugin Loading/Unloading** with AssemblyLoadContext
- ✅ **Enhanced Stability** with comprehensive error handling
- ✅ **Type Safety** with strongly-typed plugin interfaces
- ✅ **Security Hardening** with multi-level validation
- ✅ **Comprehensive Testing** with 48 tests across 7 categories

See [TDS_IMPLEMENTATION.md](TDS_IMPLEMENTATION.md) for complete implementation details.

## Features

### Core Capabilities
- Dynamic plugin discovery and loading from directories
- Runtime plugin unloading with proper memory cleanup
- Version-aware plugin management
- Thread-safe concurrent plugin execution
- Comprehensive error handling and recovery

### Type Safety
- Generic plugin interfaces: `ITypedPluginClass<TInput, TOutput>`
- Strongly-typed DTOs for plugin communication
- Execution context with logging and cancellation support
- Type-safe plugin discovery and execution

### Security
- Multi-level security validation (assembly, type, method)
- Configurable security policies (Default, Strict, Permissive)
- Digital signature verification
- Trusted path validation
- Prohibited namespace and type detection

### Performance
- High-throughput concurrent execution (>100 exec/sec)
- Fast plugin instantiation (<100ms average)
- Efficient memory management with unloading
- Performance monitoring and validation

## Quick Start

### Basic Usage

```csharp
using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.FilePlugin;
using DevelApp.RuntimePluggableClassFactory.Security;

// Create plugin loader with security validation
var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
var pluginLoader = new FilePluginLoader<IMyPluginInterface>(pluginDirectory, securityValidator);
var pluginFactory = new PluginClassFactory<IMyPluginInterface>(pluginLoader);

// Load and execute plugins
await pluginFactory.RefreshPluginsAsync();
var plugin = pluginFactory.GetInstance("MyModule", "MyPlugin");
var result = plugin.Execute("input data");

// Cleanup
pluginLoader.UnloadAllPlugins();
```

### Type-Safe Usage

```csharp
// Define strongly-typed input/output
public class MyInput { public string Data { get; set; } }
public class MyOutput { public string Result { get; set; } }

// Create typed plugin factory
var typedFactory = new TypedPluginClassFactory<IMyTypedPlugin, MyInput, MyOutput>(pluginLoader);

// Execute with type safety
var input = new MyInput { Data = "test" };
var result = typedFactory.ExecutePlugin("MyModule", "MyPlugin", input);

if (result.Success)
{
    Console.WriteLine($"Result: {result.Data.Result}");
}
```

## Plugin Development

### Basic Plugin Interface

```csharp
public interface IMyPluginInterface : IPluginClass
{
    // Custom execution method for this specific interface
    string ProcessData(string input);
}

public class MyPlugin : IMyPluginInterface
{
    // Implement IPluginClass properties
    public IdentifierString Name => "MyPlugin";
    public NamespaceString Module => "MyModule";
    public string Description => "Example plugin implementation";
    public SemanticVersionNumber Version => new SemanticVersionNumber(1, 0, 0);

    public string ProcessData(string input)
    {
        return $"Processed: {input}";
    }
}
```

### Type-Safe Plugin Interface

```csharp
public interface IMyTypedPlugin : ITypedPluginClass<MyInput, MyOutput>
{
    // Inherits both IPluginClass and ITypedPluginClass methods
}

public class MyTypedPlugin : IMyTypedPlugin
{
    // Implement IPluginClass properties
    public IdentifierString Name => "MyTypedPlugin";
    public NamespaceString Module => "MyModule";
    public string Description => "Example typed plugin implementation";
    public SemanticVersionNumber Version => new SemanticVersionNumber(1, 0, 0);
    
    public PluginExecutionResult<MyOutput> Execute(IPluginExecutionContext context, MyInput input)
    {
        context?.Logger?.LogInformation($"Processing: {input.Data}");
        
        return PluginExecutionResult<MyOutput>.CreateSuccess(new MyOutput 
        { 
            Result = $"Processed: {input.Data}" 
        });
    }
}
```

## Testing

The project includes comprehensive testing with 48 tests across 7 categories:

```bash
# Run all tests
dotnet test

# Expected: 48 tests passing
```

### Test Categories
- **Unit Tests** (4) - Core functionality
- **Stability Tests** (5) - Error handling
- **Unloading Tests** (2) - Dynamic unloading
- **Typed Plugin Tests** (8) - Type safety
- **Security Tests** (13) - Security validation
- **Integration Tests** (8) - End-to-end workflows
- **Performance Tests** (8) - Performance benchmarks

## Security

### Security Policies

```csharp
// Default security (balanced)
var defaultSettings = PluginSecuritySettings.CreateDefault();

// Strict security (high security)
var strictSettings = PluginSecuritySettings.CreateStrict();

// Permissive security (minimal restrictions)
var permissiveSettings = PluginSecuritySettings.CreatePermissive();

var validator = new DefaultPluginSecurityValidator(strictSettings);
```

### Security Features
- Assembly size limits and digital signature verification
- Prohibited namespace and type detection
- Dangerous method pattern analysis
- Trusted path validation
- Risk level assessment

## Performance Benchmarks

All performance targets are validated by automated tests:

| Metric | Target | Status |
|--------|--------|--------|
| Plugin Discovery | < 5 seconds | ✅ Validated |
| Plugin Instantiation | < 100ms avg | ✅ Validated |
| Plugin Execution | < 10ms avg | ✅ Validated |
| Concurrent Throughput | > 100 exec/sec | ✅ Validated |
| Security Validation | < 500ms avg | ✅ Validated |
| Memory Growth | < 50MB under load | ✅ Validated |

## Architecture

### Core Components
- **PluginLoadContext**: Collectible AssemblyLoadContext for proper unloading
- **FilePluginLoader**: File-based plugin discovery and loading
- **PluginClassFactory**: Core plugin factory with error handling
- **TypedPluginClassFactory**: Type-safe plugin execution
- **PluginExecutionSandbox**: Isolated execution environment
- **DefaultPluginSecurityValidator**: Comprehensive security validation
- **HybridPluginFactory** (v2.1.0+): Hybrid factory for both traditional and containerized plugins
- **ContainerizedPluginLoader** (v2.1.0+): Async loader for containerized plugins from Kubernetes/remote sources

### Key Interfaces
- `IPluginClass`: Basic plugin interface
- `ITypedPluginClass<TInput, TOutput>`: Type-safe plugin interface
- `IPluginLoader<T>`: Plugin loading interface
- `IPluginSecurityValidator`: Security validation interface
- `IContainerizedPluginOrchestrator` (v2.1.0+): Interface for containerized plugin orchestration

## Async Module-Based Plugin Loading (v2.1.0+)

### Hybrid Plugin Loading

The `HybridPluginFactory` enables seamless mixing of traditional in-process plugins with containerized plugins loaded from Kubernetes or other remote sources:

```csharp
using DevelApp.RuntimePluggableClassFactory.Containerized;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;

// Setup traditional plugin loader
var traditionalLoader = new FilePluginLoader<IMyPlugin>(pluginDirectory, securityValidator);
var traditionalFactory = new PluginClassFactory<IMyPlugin>(traditionalLoader);

// Setup containerized plugin orchestrator (e.g., Kubernetes)
var containerizedOrchestrator = new KubernetesPluginOrchestrator(/* ... */);

// Create hybrid factory
var hybridFactory = new HybridPluginFactory<IMyPlugin>(
    traditionalFactory,
    containerizedOrchestrator,
    logger,
    new HybridPluginFactoryOptions 
    { 
        PreferContainerized = true,
        AutoDeployContainerized = false 
    });

// Load plugins from either source
var plugin = await hybridFactory.GetPluginAsync(
    new NamespaceString("MyCompany.Plugins"),
    new IdentifierString("DataProcessor"),
    version: new SemanticVersionNumber(1, 0, 0),
    executionMode: PluginExecutionMode.Auto);

// List all available plugins
var availablePlugins = await hybridFactory.ListAvailablePluginsAsync();
foreach (var pluginInfo in availablePlugins)
{
    Console.WriteLine($"{pluginInfo.ModuleName}.{pluginInfo.PluginName} ({pluginInfo.ExecutionMode})");
}
```

### Module Identification Types

The following types are now exposed in the public API for implementing custom async module-based plugin loading:

- **PluginIdentifier**: Identifies plugins by namespace, name, and version
- **PluginInfo**: Plugin metadata including execution mode and container information
- **PluginExecutionMode**: Enum values - Auto, Traditional, Containerized
- **ContainerizedPluginInfo**: Metadata for deployed containerized plugins
- **ContainerizedPluginLoader<T>**: Async loader for containerized plugins
- **ContainerizedPluginLoaderOptions**: Configuration for async loading

These types enable dynamic module loading from Kubernetes/remote sources as an alternative to traditional directory-based scanning.

## Documentation

- [TDS Implementation Details](TDS_IMPLEMENTATION.md) - Complete TDS implementation documentation
- [Testing Strategy](TestingStrategy.md) - Comprehensive testing approach

## Requirements

- .NET 8.0 or later
- xUnit (for testing)
- Compatible with Windows, Linux, and macOS

## Installation

```bash
# Clone repository
git clone https://github.com/DevelApp-ai/RuntimePluggableClassFactory.git

# Build solution
dotnet build RuntimePluggableClassFactory.sln

# Run tests
dotnet test
```

## Why Use RuntimePluggableClassFactory

### Original Use Cases (Still Supported)
- **Websites/APIs without persisted session state**: Avoid session loss during dependency injection reloads
- **Separating frequently modified extensions**: Enable runtime loading and replacement without application restart
- **A/B testing for services**: Load different plugin versions for testing
- **Fault isolation**: Plugin errors don't crash the entire application
- **Easier testing**: Test extensions separately from other concerns

### Enhanced TDS Capabilities
- **Dynamic Plugin Management**: Load and unload plugins without application restart
- **Type Safety**: Strongly-typed plugin interfaces eliminate runtime type errors
- **Security Hardening**: Multi-level validation prevents malicious plugin execution
- **Performance Optimization**: High-throughput concurrent execution with memory management
- **Comprehensive Testing**: 48 automated tests ensure reliability and performance

## Contributing

1. Fork the repository
2. Create a feature branch
3. Implement changes with tests
4. Ensure all 48 tests pass
5. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

For questions, issues, or support:
- Review the comprehensive test suite (48 tests)
- Check the [TDS Implementation Guide](TDS_IMPLEMENTATION.md)
- Examine the [Testing Strategy](TestingStrategy.md)
- Create an issue on GitHub

---

**TDS Implementation Status**: ✅ Complete - All requirements implemented and validated
