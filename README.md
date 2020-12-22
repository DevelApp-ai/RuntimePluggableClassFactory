# RuntimePluggableClassFactory
Factory that loads classes from provided assemblies having a specific interface

## TODO
* Split Factory into assembly loader and factory
* Assembly loader based on Nuget
  * Automatic updates support 
  * Triggered updates support
  * Use Semantic versioning so that 1.2.x will return the latest as it is only corrections without breaking changes
  * Investigate if Semantic versioning only makes sense for interfaces and not for implementations
* Factory made for each 
