using PluginImplementations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace RuntimePluggableClassFactory.PluginHost
{
    /// <summary>
    /// Container host binary for the issue #28 measurement harness
    /// (M2 container leg + M3 migration study).
    ///
    /// This process stands in for the plugin container: it is a separate OS
    /// process holding the plugin assembly, isolating it from the calling
    /// runtime (separate memory, separate lifetime, real IPC on every call).
    /// A Docker/Kubernetes deployment would run this same binary as the image
    /// ENTRYPOINT; what that adds on top (image pull, sandbox/cgroup overhead,
    /// network hop for non-local pods) is NOT measured by this harness and is
    /// explicitly labeled as such in the committed data.
    ///
    /// Protocol (one JSON document per line, stdin -> stdout, stderr for
    /// diagnostics only):
    ///   request  {"op":"ping"}
    ///   response {"op":"pong"}
    ///   request  {"op":"execute","id":"...","input":"..."}
    ///   response {"id":"...","success":true,"data":&lt;bool&gt;} or
    ///            {"id":"...","success":false,"error":"..."}
    ///   request  {"op":"shutdown"}
    ///   response {"op":"shutdown-ack"} then exit 0
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var pluginPaths = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--plugin":
                        pluginPaths.Add(args[++i]);
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown argument '{args[i]}'.");
                        return 2;
                }
            }

            if (pluginPaths.Count == 0)
            {
                Console.Error.WriteLine("No --plugin path given.");
                return 2;
            }

            try
            {
                var plugins = new List<ISpecificInterface>();
                foreach (var path in pluginPaths)
                {
                    plugins.AddRange(LoadPlugins(path));
                }
                if (plugins.Count == 0)
                {
                    Console.Error.WriteLine("No ISpecificInterface plugins found in the given assemblies.");
                    return 3;
                }
                Serve(plugins);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal: {ex}");
                return 4;
            }
        }

        /// <summary>
        /// Loads the plugin assembly and instantiates every concrete
        /// ISpecificInterface class with a parameterless constructor.
        /// </summary>
        private static IEnumerable<ISpecificInterface> LoadPlugins(string pluginPath)
        {
            var fullPath = Path.GetFullPath(pluginPath);
            var assembly = Assembly.LoadFrom(fullPath);
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!typeof(ISpecificInterface).IsAssignableFrom(type)) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                yield return (ISpecificInterface)Activator.CreateInstance(type)!;
            }
        }

        private static void Serve(IReadOnlyList<ISpecificInterface> plugins)
        {
            // Instantiated once - a stateful plugin keeps its state across
            // requests, exactly like the in-process instance in the harness.
            var plugin = plugins[0];

            string? line;
            while ((line = Console.In.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string response;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var op = root.TryGetProperty("op", out var opEl) ? opEl.GetString() : null;

                    switch (op)
                    {
                        case "ping":
                            response = "{\"op\":\"pong\"}";
                            break;

                        case "execute":
                        {
                            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                            var input = root.TryGetProperty("input", out var inputEl) ? inputEl.GetString() ?? "" : "";
                            var result = plugin.Execute(input);
                            response = JsonSerializer.Serialize(new
                            {
                                id,
                                success = true,
                                data = result
                            });
                            break;
                        }

                        case "shutdown":
                            Console.Out.WriteLine("{\"op\":\"shutdown-ack\"}");
                            Console.Out.Flush();
                            return;

                        default:
                            response = "{\"success\":false,\"error\":\"unknown op\"}";
                            break;
                    }
                }
                catch (Exception ex)
                {
                    response = JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }

                Console.Out.WriteLine(response);
                Console.Out.Flush();
            }
        }
    }
}
