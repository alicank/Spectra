using Spectra.Contracts.Mcp;

namespace Spectra.Registration;

/// <summary>
/// Fluent builder for <see cref="McpServerConfig"/>.
/// Used by <see cref="SpectraBuilder.AddMcpServer(string, Action{McpServerConfigBuilder})"/>.
/// </summary>
public sealed class McpServerConfigBuilder
{
    private readonly string _name;
    private McpTransportType _transport = McpTransportType.Stdio;
    private string? _command;
    private readonly List<string> _arguments = [];
    private string? _workingDirectory;
    private readonly Dictionary<string, string> _env = new();
    private bool _inheritEnv;
    private string? _url;
    private readonly Dictionary<string, string> _headers = new();
    private List<string>? _allowedTools;
    private List<string>? _deniedTools;
    private bool _readOnly;
    private int _maxCallsPerSession;
    private int _maxConcurrentCalls;
    private int _maxResponseSizeBytes = 1_048_576;
    private decimal _costPerCall;
    private bool _requireApproval;
    private McpResilienceOptions? _resilience;

    public McpServerConfigBuilder(string name) => _name = name;

    // ── Transport selection ──

    /// <summary>
    /// Use stdio transport: spawn a process and communicate over stdin/stdout.
    /// </summary>
    public McpServerConfigBuilder UseStdio(string command, params string[] arguments)
    {
        _transport = McpTransportType.Stdio;
        _command = command;
        _arguments.Clear();
        _arguments.AddRange(arguments);
        return this;
    }

    /// <summary>
    /// Use SSE transport: connect to an HTTP endpoint with Server-Sent Events.
    /// </summary>
    public McpServerConfigBuilder UseSse(string url)
    {
        _transport = McpTransportType.Sse;
        _url = url;
        return this;
    }

    /// <summary>
    /// Use streamable-HTTP transport.
    /// </summary>
    public McpServerConfigBuilder UseHttp(string url)
    {
        _transport = McpTransportType.Http;
        _url = url;
        return this;
    }

    // ── Environment ──

    public McpServerConfigBuilder WithWorkingDirectory(string dir) { _workingDirectory = dir; return this; }
    public McpServerConfigBuilder WithEnvironment(string key, string value) { _env[key] = value; return this; }
    public McpServerConfigBuilder WithInheritEnvironment(bool inherit = true) { _inheritEnv = inherit; return this; }

    // ── HTTP headers ──

    public McpServerConfigBuilder WithHeader(string key, string value) { _headers[key] = value; return this; }
    public McpServerConfigBuilder WithBearerToken(string token) => WithHeader("Authorization", $"Bearer {token}");

    // ── Guardrails ──

    public McpServerConfigBuilder WithAllowedTools(params string[] tools) { _allowedTools = [.. tools]; return this; }
    public McpServerConfigBuilder WithDeniedTools(params string[] tools) { _deniedTools = [.. tools]; return this; }
    public McpServerConfigBuilder AsReadOnly(bool readOnly = true) { _readOnly = readOnly; return this; }
    public McpServerConfigBuilder WithMaxCallsPerSession(int max) { _maxCallsPerSession = max; return this; }
    public McpServerConfigBuilder WithMaxConcurrentCalls(int max) { _maxConcurrentCalls = max; return this; }
    public McpServerConfigBuilder WithMaxResponseSize(int bytes) { _maxResponseSizeBytes = bytes; return this; }
    public McpServerConfigBuilder WithCostPerCall(decimal cost) { _costPerCall = cost; return this; }
    public McpServerConfigBuilder WithRequireApproval(bool require = true) { _requireApproval = require; return this; }

    // ── Resilience ──

    public McpServerConfigBuilder WithResilience(Action<McpResilienceOptions> configure)
    {
        var options = new McpResilienceOptions();
        // McpResilienceOptions is a record — apply via with expression
        configure(options);
        _resilience = options;
        return this;
    }

    public McpServerConfigBuilder WithResilience(McpResilienceOptions options)
    {
        _resilience = options;
        return this;
    }

    internal McpServerConfig Build() => new()
    {
        Name = _name,
        Transport = _transport,
        Command = _command,
        Arguments = [.. _arguments],
        WorkingDirectory = _workingDirectory,
        EnvironmentVariables = new(_env),
        InheritEnvironment = _inheritEnv,
        Url = _url,
        Headers = new(_headers),
        AllowedTools = _allowedTools,
        DeniedTools = _deniedTools,
        ReadOnly = _readOnly,
        MaxCallsPerSession = _maxCallsPerSession,
        MaxConcurrentCalls = _maxConcurrentCalls,
        MaxResponseSizeBytes = _maxResponseSizeBytes,
        CostPerCall = _costPerCall,
        RequireApproval = _requireApproval,
        Resilience = _resilience
    };
}