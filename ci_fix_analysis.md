# CI Failure Analysis

## Root Cause
The tests are calling methods that don't exist or have been renamed:

1. **`ListAllPossiblePluginsAsync()`** - This method exists in the PluginLoader but not directly in PluginClassFactory. The PluginClassFactory has `GetPossiblePlugins()` instead.

2. **`PluginInstantiationError`** - This event is actually named `PluginInstantiationFailed` in the code.

3. **Type conversion issues** - Some implicit conversions are failing due to nullable types.

## Fixes Needed

### 1. Fix Method Names in Tests
- Replace `ListAllPossiblePluginsAsync()` with `GetPossiblePlugins()`
- Replace `PluginInstantiationError` with `PluginInstantiationFailed`

### 2. Fix Type Conversion Issues
- Handle nullable type conversions properly
- Fix tuple comparison issues

### 3. Update Workflow Test Filter
- The current test filter excludes IntegrationTests and PerformanceTests, but they're still being compiled and causing errors
- Need to either fix the tests or improve the filter

