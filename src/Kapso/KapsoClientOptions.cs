using System.ComponentModel.DataAnnotations;

namespace Kapso;

/// <summary>
/// Configuration for <see cref="KapsoClient"/>.
/// </summary>
public sealed class KapsoClientOptions
{
    /// <summary>
    /// Configuration section bound by <c>AddKapso(IConfiguration)</c>.
    /// </summary>
    public const string DefaultConfigurationSection = "Kapso";

    /// <summary>
    /// Project API key, sent as <c>X-API-Key</c>. One key authenticates all three
    /// Kapso APIs. Found in the dashboard under Integrations &gt; API keys.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "A Kapso API key is required.")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Base address of the WhatsApp (Meta proxy) API. The default embeds the Graph
    /// API version the vendored OpenAPI description was generated against; override
    /// it to target a different version or a test endpoint.
    /// </summary>
    public Uri WhatsAppEndpoint { get; set; } = KapsoEndpoints.WhatsApp;

    /// <summary>
    /// Base address of the Platform API. The Workflows and Agent surfaces are the
    /// same service and share this address.
    /// </summary>
    public Uri PlatformEndpoint { get; set; } = KapsoEndpoints.Platform;

    /// <summary>
    /// Retry and timeout behaviour. See <see cref="KapsoRetryOptions"/> for why
    /// retries are not applied uniformly across HTTP methods.
    /// </summary>
    public KapsoRetryOptions Retry { get; set; } = new();

    /// <summary>
    /// Hosts the API key may be sent to. Guards against a redirect carrying the
    /// credential to somewhere else. Defaults to the hosts of the two endpoints.
    /// </summary>
    public IList<string> AllowedHosts { get; } = [];

    internal IReadOnlyCollection<string> ResolveAllowedHosts() =>
        AllowedHosts.Count > 0
            ? [.. AllowedHosts]
            : [WhatsAppEndpoint.Host, PlatformEndpoint.Host];
}
