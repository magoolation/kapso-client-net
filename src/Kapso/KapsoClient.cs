using System.Reflection;

using Kapso.Generated.Agent;
using Kapso.Generated.Platform;
using Kapso.Generated.WhatsApp.BusinessAccounts;
using Kapso.Generated.WhatsApp.Flows;
using Kapso.Generated.WhatsApp.Media;
using Kapso.Generated.WhatsApp.PhoneNumbers;
using Kapso.Generated.Workflows;
using Kapso.Http;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Kapso;

/// <summary>
/// Entry point to the Kapso APIs.
/// </summary>
/// <remarks>
/// One project API key authenticates all three APIs, so a single client covers
/// them. Construct it directly, or register it with
/// <c>services.AddKapso(...)</c>; both produce the same configuration.
/// </remarks>
/// <example>
/// <code>
/// using var kapso = new KapsoClient("your-api-key");
/// var customers = await kapso.Platform.Customers.GetAsync();
/// </code>
/// </example>
public sealed class KapsoClient : IDisposable
{
    private static readonly string UserAgent = BuildUserAgent();

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly HttpClientRequestAdapter[] _adapters;
    private readonly KapsoRateLimitTracker _rateLimitTracker;
    private bool _disposed;

    /// <summary>
    /// Creates a client with the default endpoints and resilience settings.
    /// </summary>
    /// <param name="apiKey">Project API key, sent as <c>X-API-Key</c>.</param>
    /// <param name="loggerFactory">
    /// Where to send diagnostics. Supply one and a failed response is logged with
    /// the body the server sent, which is otherwise lost: Kiota keeps only the
    /// status code unless the body matches an error schema in the OpenAPI
    /// description, and Kapso's errors frequently do not.
    /// </param>
    public KapsoClient(string apiKey, ILoggerFactory? loggerFactory = null)
        : this(new KapsoClientOptions { ApiKey = apiKey }, loggerFactory)
    {
    }

    /// <summary>
    /// Creates a client that owns its <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>
    /// Prefer <c>services.AddKapso(...)</c> in a hosted application so the
    /// connection pool is managed by <c>IHttpClientFactory</c>. This constructor
    /// suits console tools and tests: the client is long-lived and disposable, and
    /// the handler it builds recycles pooled connections so it does not go stale
    /// against DNS changes.
    /// </remarks>
    /// <param name="options">Endpoints, credentials and retry behaviour.</param>
    /// <param name="loggerFactory">
    /// Where to send diagnostics. See the other constructor for why it is worth
    /// supplying.
    /// </param>
    public KapsoClient(KapsoClientOptions options, ILoggerFactory? loggerFactory = null)
        : this(PrepareOwned(options, loggerFactory))
    {
    }

    private KapsoClient(OwnedSetup setup)
        : this(setup.Options, setup.HttpClient, setup.Tracker, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Constructor used by dependency injection. The <see cref="HttpClient"/> comes
    /// from <c>IHttpClientFactory</c>, which owns its lifetime, so this client does
    /// not dispose it.
    /// </summary>
    /// <remarks>Prefer <c>services.AddKapso(...)</c> over calling this directly.</remarks>
    public KapsoClient(
        HttpClient httpClient,
        IOptions<KapsoClientOptions> options,
        KapsoRateLimitTracker rateLimitTracker)
        : this(
            (options ?? throw new ArgumentNullException(nameof(options))).Value,
            httpClient,
            rateLimitTracker,
            ownsHttpClient: false)
    {
    }

    private KapsoClient(
        KapsoClientOptions options,
        HttpClient httpClient,
        KapsoRateLimitTracker rateLimitTracker,
        bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(rateLimitTracker);

        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _rateLimitTracker = rateLimitTracker;

        if (httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        }

        // The key travels only to the hosts the options name, so a redirect cannot
        // carry it somewhere else.
        var authentication = new ApiKeyAuthenticationProvider(
            options.ApiKey,
            "X-API-Key",
            ApiKeyAuthenticationProvider.KeyLocation.Header,
            [.. options.ResolveAllowedHosts()]);

        // Each generated client needs its own adapter. The generated constructor
        // copies RequestAdapter.BaseUrl into its path parameters and only assigns
        // it when unset, so sharing one adapter between the WhatsApp and Platform
        // clients would silently send one of them to the other's base address.
        // Adapters are cheap; the HttpClient underneath them is shared.
        var whatsAppUrl = Normalize(options.WhatsAppEndpoint);
        var platformUrl = Normalize(options.PlatformEndpoint);

        var phoneNumbers = Adapter(authentication, whatsAppUrl);
        var businessAccounts = Adapter(authentication, whatsAppUrl);
        var flows = Adapter(authentication, whatsAppUrl);
        var media = Adapter(authentication, whatsAppUrl);
        var platform = Adapter(authentication, platformUrl);
        var workflows = Adapter(authentication, platformUrl);
        var agent = Adapter(authentication, platformUrl);

        _adapters = [phoneNumbers, businessAccounts, flows, media, platform, workflows, agent];

        WhatsApp = new KapsoWhatsAppApi(
            new WhatsAppPhoneNumbersClient(phoneNumbers),
            new WhatsAppBusinessAccountsClient(businessAccounts),
            new WhatsAppFlowsClient(flows),
            new WhatsAppMediaClient(media));

        Platform = new PlatformClient(platform);
        Workflows = new WorkflowsClient(workflows);
        Agent = new AgentClient(agent);

        HttpClientRequestAdapter Adapter(IAuthenticationProvider auth, string baseUrl) =>
            new(auth, httpClient: _httpClient) { BaseUrl = baseUrl };
    }

    /// <summary>Send and read WhatsApp messages, templates, media and flows.</summary>
    public KapsoWhatsAppApi WhatsApp { get; }

    /// <summary>
    /// Customers, phone number provisioning, setup links, broadcasts and webhooks.
    /// </summary>
    public PlatformClient Platform { get; }

    /// <summary>
    /// Workflow executions, triggers and serverless functions. The same service as
    /// <see cref="Platform"/>; Kapso documents them apart.
    /// </summary>
    public WorkflowsClient Workflows { get; }

    /// <summary>Kapso Agent runs, sessions and modes.</summary>
    public AgentClient Agent { get; }

    /// <summary>
    /// Rate limit state from the most recent response, or <see langword="null"/>
    /// before the first call. Back off as
    /// <see cref="KapsoRateLimitSnapshot.Remaining"/> nears zero rather than
    /// waiting for a 429.
    /// </summary>
    public KapsoRateLimitSnapshot? RateLimit => _rateLimitTracker.Latest;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Each adapter only disposes an HttpClient it created itself, so this does
        // not touch the shared one.
        foreach (var adapter in _adapters)
        {
            adapter.Dispose();
        }

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static void Validate(KapsoClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new ArgumentException(
                "A Kapso API key is required. Get one from the dashboard under Integrations > API keys.",
                nameof(options));
        }
    }

    /// <summary>
    /// Everything the owning constructor has to build before it can chain to the
    /// shared one. A separate step because a constructor initializer cannot hold
    /// this much work legibly.
    /// </summary>
    private readonly record struct OwnedSetup(
        KapsoClientOptions Options,
        HttpClient HttpClient,
        KapsoRateLimitTracker Tracker);

    private static OwnedSetup PrepareOwned(KapsoClientOptions options, ILoggerFactory? loggerFactory)
    {
        Validate(options);
        var tracker = new KapsoRateLimitTracker();
        return new OwnedSetup(options, CreateOwnedHttpClient(options, tracker, loggerFactory), tracker);
    }

    private static HttpClient CreateOwnedHttpClient(
        KapsoClientOptions options,
        KapsoRateLimitTracker tracker,
        ILoggerFactory? loggerFactory)
    {
        HttpMessageHandler handler = new SocketsHttpHandler
        {
            // Lets the pool notice DNS changes without recycling the client.
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        handler = new KapsoRateLimitTrackingHandler(tracker, TimeProvider.System) { InnerHandler = handler };

        // Innermost, so every attempt is reported rather than only the last.
        if (loggerFactory is not null)
        {
            handler = new KapsoErrorLoggingHandler(loggerFactory.CreateLogger<KapsoErrorLoggingHandler>())
            {
                InnerHandler = handler,
            };
        }

        if (!options.Retry.Enabled)
        {
            return new HttpClient(handler, disposeHandler: true);
        }

        // Inside the resilience handler, so it runs on every attempt and can see
        // the resilience context.
        handler = new KapsoResilienceContextHandler { InnerHandler = handler };

        var resilience = KapsoResiliencePipeline.CreateHandler(options.Retry);
        resilience.InnerHandler = handler;

        return new HttpClient(resilience, disposeHandler: true)
        {
            // The pipeline owns timeouts. Leaving HttpClient's own 100 second
            // default in place would cut across the retry budget.
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static string Normalize(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        // The generated URL templates append "/segment", so a trailing slash here
        // would produce a double slash.
        return endpoint.AbsoluteUri.TrimEnd('/');
    }

    private static string BuildUserAgent()
    {
        var version = typeof(KapsoClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

        // Strip the build metadata MinVer appends; the commit hash is noise here.
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            version = version[..plus];
        }

        return $"kapso-dotnet/{version} ({Environment.OSVersion.Platform}; .NET {Environment.Version})";
    }
}
