using System.Reflection;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Codai.Tools.Plugins;

/// <summary>Loads configured tool plugin assemblies into the host service collection.</summary>
public static class ToolPluginLoader
{
    /// <summary>Loads every configured plugin and registers its discovered plugins.</summary>
    /// <param name="services">Host service collection.</param>
    /// <param name="paths">Absolute or working-directory-relative assembly paths.</param>
    /// <param name="baseDirectory">Base directory for relative paths.</param>
    /// <param name="disabledPaths">Configured plugin paths that must not be loaded.</param>
    public static IReadOnlyList<string> Register(
        IServiceCollection services,
        IEnumerable<string> paths,
        string baseDirectory,
        IEnumerable<string>? disabledPaths = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var disabled = (disabledPaths ?? []).Select(path => Path.GetFullPath(path, baseDirectory))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var loaded = new List<string>();
        var configuredPaths = paths
            .Concat(Directory.Exists(Path.Combine(baseDirectory, ".codai", "plugins"))
                ? Directory.EnumerateFiles(
                    Path.Combine(baseDirectory, ".codai", "plugins"),
                    "*.dll",
                    SearchOption.TopDirectoryOnly)
                : [])
            .Select(path => Path.GetFullPath(path, baseDirectory))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var configuredPath in configuredPaths)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                continue;
            }

            var path = Path.GetFullPath(configuredPath, baseDirectory);
            if (disabled.Contains(path))
            {
                continue;
            }
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Configured plugin was not found: {path}", path);
            }

            var assembly = Assembly.LoadFrom(path);
            var pluginTypes = assembly.GetTypes()
                .Where(type => typeof(IToolPlugin).IsAssignableFrom(type))
                .Where(type => !type.IsAbstract && !type.IsInterface)
                .ToArray();

            foreach (var pluginType in pluginTypes)
            {
                if (Activator.CreateInstance(pluginType) is not IToolPlugin plugin)
                {
                    throw new InvalidOperationException(
                        $"Plugin type '{pluginType.FullName}' could not be created.");
                }

                plugin.Register(services);
                if (plugin is IPluginCommandProvider commandProvider)
                {
                    services.AddSingleton(commandProvider);
                }

                if (plugin is IPluginAgentHook hookProvider)
                {
                    services.AddSingleton(hookProvider);
                }
            }

            loaded.Add(path);
        }

        return loaded;
    }
}