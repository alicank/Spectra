using System.Reflection;
using Spectra.Contracts.Tools;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Scans assemblies for <see cref="ITool"/> implementations decorated with
/// <see cref="SpectraToolAttribute"/> and instantiates them via their parameterless constructor.
/// </summary>
public static class ToolDiscovery
{
    /// <summary>
    /// Discovers and instantiates all tools in the given assemblies.
    /// Classes must implement <see cref="ITool"/>, be concrete (non-abstract),
    /// and be decorated with <see cref="SpectraToolAttribute"/>.
    /// </summary>
    public static IReadOnlyList<ITool> DiscoverTools(params Assembly[] assemblies)
    {
        var tools = new List<ITool>();

        foreach (var assembly in assemblies)
        {
            var toolTypes = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false }
                            && typeof(ITool).IsAssignableFrom(t)
                            && t.GetCustomAttribute<SpectraToolAttribute>() is not null);

            foreach (var type in toolTypes)
            {
                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor is null)
                    throw new InvalidOperationException(
                        $"Tool type '{type.FullName}' is decorated with [SpectraTool] but has no parameterless constructor. " +
                        $"Register it manually via AddTool() instead.");

                var tool = (ITool)ctor.Invoke(null);
                tools.Add(tool);
            }
        }

        return tools;
    }

    /// <summary>
    /// Discovers tools in the assembly containing <typeparamref name="T"/>.
    /// </summary>
    public static IReadOnlyList<ITool> DiscoverTools<T>()
        => DiscoverTools(typeof(T).Assembly);
}