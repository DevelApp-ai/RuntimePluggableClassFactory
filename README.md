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

## TODO
* Include documentation on how to use the library as it is only existing in code right now
* Massively extend the unit and integration testing
* Assembly loader based on Nuget
  * Automatic updates support 
  * Triggered updates support
  * Use Semantic versioning so that 1.2.x will return the latest as it is only corrections without breaking changes
  * Investigate if Semantic versioning only makes sense for interfaces and not for implementations
* Unload ApplicationLoadContext when not any longer in use
* Perhaps use the (Dot Net Tools)[https://github.com/RicoSuter/DNT] to switch between project and nuget references if I can get it to work in GitHub Actions
