namespace Spectra.Contracts.Tools;

/// <summary>
/// Marks an <see cref="ITool"/> implementation for auto-discovery
/// when scanning assemblies via <c>AddToolsFromAssembly</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SpectraToolAttribute : Attribute;