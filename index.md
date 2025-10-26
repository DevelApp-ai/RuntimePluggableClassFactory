---
layout: default
title: RuntimePluggableClassFactory
description: A comprehensive .NET library for dynamic plugin loading, execution, and management with enhanced stability, type safety, and security features.
---

# RuntimePluggableClassFactory

A comprehensive .NET library for dynamic plugin loading, execution, and management with enhanced stability, type safety, and security features.

## 🚀 TDS Implementation Complete

This project has been enhanced with a complete Technical Design Specification (TDS) implementation featuring:

- ✅ **Dynamic Plugin Loading/Unloading** with AssemblyLoadContext
- ✅ **Enhanced Stability** with comprehensive error handling
- ✅ **Type Safety** with strongly-typed plugin interfaces
- ✅ **Security Hardening** with multi-level validation
- ✅ **Comprehensive Testing** with 48 tests across 7 categories

## Quick Navigation

- **[📖 Complete Documentation](README)** - Full project documentation and usage examples
- **[🔧 TDS Implementation Details](TDS_IMPLEMENTATION)** - Technical design specification implementation
- **[🧪 Testing Strategy](TestingStrategy)** - Comprehensive testing approach
- **[📋 Changelog](CHANGELOG)** - Version history and updates
- **[📥 GitHub Repository](https://github.com/DevelApp-ai/RuntimePluggableClassFactory)** - Source code and issues

## Key Features

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
var result = plugin.ProcessData("input data");

// Cleanup
pluginLoader.UnloadAllPlugins();
```

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

---

**TDS Implementation Status**: ✅ Complete - All requirements implemented and validated