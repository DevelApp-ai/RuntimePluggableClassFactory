# RuntimePluggableClassFactory
This library has been made to make dependency injection at runtime. The main idea is to use a plugin folder from where the factory can load dll's. Factory that loads classes from provided assemblies having a specific user implemented interface inheriting from the provided IPluginClass interface.

## Why use it and where
* Websites/APIs that are running without persisted session state behind a login
  * As Asp.Net is implemented with compiled code the website will reload when a new dependency injection needs to be reloaded causing all session state information to disappear and typically logged in users will be expelled
* Separating much modified extensions from the running code when making a program execution framework so that 
  * application can internally contain the Continouos Deployment approval
  * extensions can be loaded and replaced at runtime
  * making it possible to make a/b testing for services
  * extension loading errors are not taking down the whole application and only the affected extension
  * making it easier to test extensions separated from other concerns

## Design
* You call a class factory (PluginClassFactory) to get a specific instance of a class

## State
* I have used it for a couple of projects which run in production

## Plugin libraries
* The pluging needs to have all the dependencies deployed with it having all in one plugin folder including the IPluginClass

## Bug Found
* There is only support for one dll per interface as an odd cast error happens. Perhaps a trick like the one done with IPluginClass needs to be done for the special loaded interfaces so they are in the same context

## TODO
* Include documentation on how to use the library as it is only existing in code right now
* Massively extend the unit and integration testing
* A plugin folder hierarchy to support existing functionality but making plugins easier to manage as all plugins and dependencies are in the same folder
* Assembly loader based on Nuget
  * Automatic updates support 
  * Triggered updates support
  * Use Semantic versioning so that 1.2.x will return the latest as it is only corrections without breaking changes
  * Investigate if Semantic versioning only makes sense for interfaces and not for implementations
* Separate each assembly loaded code to limit the possibility of the plugin being able to crash the main application. This is done but more investigation needs to be put in ApplicationLoadContext
* Make a file with accepted pluins to load to filter which plugins have been accepted
* Making it easier to make extensions with runtime errors not take down the whole application if possible. It seems to be easier to do when using the library than in the library itself
* Perhaps use the (Dot Net Tools)[https://github.com/RicoSuter/DNT] to switch between project and nuget references if I can get it to work in GitHub Actions
