# Release Fix Notes - v2.0.1

## Problem Summary
The NuGet package was incorrectly published with version `v2.0.1-ci.28` instead of the clean `2.0.1` version due to GitVersion configuration issues and a deprecated GitHub Actions setup.

## Changes Made

### 1. GitVersion Configuration Update
- **File**: `GitVersion.yml`
- **Changes**: 
  - Updated syntax from v5.x to v6.x format
  - Changed `tag` property to `label` 
  - Added required `regex` patterns for branch detection
  - Simplified configuration to essential properties only

### 2. GitHub Actions Workflow Update
- **File**: `.github/workflows/dotnet-cicd.yml`
- **Changes**:
  - Replaced deprecated `actions/create-release@v1` with `softprops/action-gh-release@v2`
  - Added NuGet package files to the release assets
  - Improved release creation reliability

## Expected Results

### When merged to master branch:
1. **GitVersion** will produce clean version `2.0.1` without CI suffixes
2. **NuGet packages** will be published to NuGet.org with version `2.0.1`
3. **GitHub Release** will be created with tag `v2.0.1` and include NuGet package files
4. **Release notes** will include proper package links and feature descriptions

### For feature/hotfix branches:
- Will continue to get pre-release versions with appropriate labels
- No impact on existing development workflow

## Manual Steps Required (if needed)

### If the existing `v2.0.1-ci.28` release interferes:
1. Delete the problematic tag and release (optional):
   ```bash
   git tag -d v2.0.1-ci.28
   git push origin :refs/tags/v2.0.1-ci.28
   ```
2. Delete the GitHub release `v2.0.1-ci.28` via GitHub UI

### To trigger a new clean release:
1. Merge this PR to master
2. The workflow will automatically:
   - Calculate version `2.0.1` 
   - Build and test the solution
   - Create NuGet packages with version `2.0.1`
   - Publish packages to NuGet.org
   - Create GitHub release `v2.0.1` with package files

## Verification Steps

After the workflow runs on master:
1. Check GitHub Releases for clean `v2.0.1` release
2. Verify NuGet.org has packages with version `2.0.1`:
   - [DevelApp.RuntimePluggableClassFactory](https://www.nuget.org/packages/DevelApp.RuntimePluggableClassFactory/2.0.1)
   - [DevelApp.RuntimePluggableClassFactory.Interface](https://www.nuget.org/packages/DevelApp.RuntimePluggableClassFactory.Interface/2.0.1)
3. Confirm package metadata and dependencies are correct

## Technical Details

### GitVersion Changes:
- **Before**: Used v5.x syntax with unsupported properties
- **After**: Clean v6.x syntax with proper branch regex patterns
- **Master branch**: Produces clean versions without pre-release labels
- **Feature branches**: Maintains pre-release labeling for development

### GitHub Actions Changes:
- **Before**: `actions/create-release@v1` (deprecated)
- **After**: `softprops/action-gh-release@v2` (modern, maintained)
- **Benefits**: Better reliability, asset upload support, continued maintenance

This fix ensures proper semantic versioning and package publishing for future releases.