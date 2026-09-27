namespace Kapso;

/// <summary>
/// Default base addresses, taken from the <c>servers</c> entry of each vendored
/// OpenAPI description.
/// </summary>
public static class KapsoEndpoints
{
    /// <summary>WhatsApp (Meta proxy) API.</summary>
    public static Uri WhatsApp { get; } = new("https://api.kapso.ai/meta/whatsapp/v24.0");

    /// <summary>Platform API, shared by the Workflows and Agent surfaces.</summary>
    public static Uri Platform { get; } = new("https://api.kapso.ai/platform/v1");
}
