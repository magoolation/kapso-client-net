# Kapso .NET SDK

Typed .NET 10 client for the [Kapso](https://docs.kapso.ai) APIs, generated from
Kapso's official OpenAPI descriptions with [Kiota](https://aka.ms/kiota).

Kapso publishes a TypeScript SDK, a CLI and n8n nodes. This covers .NET.

```csharp
using var kapso = new KapsoClient("your-api-key");

await kapso.WhatsApp.PhoneNumbers["15550001111"].Messages.PostAsync(message);
await kapso.Platform.Customers.GetAsync();
```

## Install

```bash
dotnet add package Kapso.Client.Net
```

## What it covers

One project API key authenticates all three APIs, so one client covers them.

| Surface | Kapso API | Base address |
| --- | --- | --- |
| `kapso.WhatsApp` | WhatsApp (Meta proxy) | `api.kapso.ai/meta/whatsapp/v24.0` |
| `kapso.Platform` | Platform | `api.kapso.ai/platform/v1` |
| `kapso.Workflows` | Workflows and Functions | `api.kapso.ai/platform/v1` |
| `kapso.Agent` | Kapso Agent | `api.kapso.ai/platform/v1` |

`kapso.WhatsApp` is grouped by the resource each route is rooted at, because
Kapso mirrors Meta's Graph API where the leading path segment is an opaque ID:

```csharp
await kapso.WhatsApp.PhoneNumbers["15550001111"].Messages.PostAsync(message);
await kapso.WhatsApp.BusinessAccounts["102290129340398"].Message_templates.GetAsync();
await kapso.WhatsApp.Flows["flow-id"].Publish.PostAsync();
await kapso.WhatsApp.Media["media-id"].GetAsync();
```

## Dependency injection

```csharp
builder.Services.AddKapso(builder.Configuration);   // binds the "Kapso" section
```

```json
{
  "Kapso": {
    "ApiKey": "your-api-key"
  }
}
```

`AddKapso` returns the `IHttpClientBuilder`, so the application can add its own
handlers or replace the resilience strategy. Configuration is validated at
startup, not on the first call.

## Pagination

List endpoints return one page. `EnumerateAsync` follows the rest:

```csharp
await foreach (var customer in kapso.Platform.Customers.EnumerateAsync(cancellationToken))
{
    Console.WriteLine(customer.Name);
}
```

Pages are fetched lazily. For an endpoint without an `EnumerateAsync` extension,
use `KapsoPagination.ByPageAsync` or `ByCursorAsync` directly.

## Webhooks

Webhook payloads are not in Kapso's OpenAPI descriptions, so these types are
hand-written from the documentation.

Verify against the **raw body**. Re-serializing a parsed object produces
different bytes and the signature will not match.

```csharp
app.MapPost("/webhooks/kapso", async (HttpRequest request) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);

    var result = new KapsoWebhookReader(secret).Read(
        buffer.ToArray(),
        KapsoWebhookHeaders.From(name => request.Headers[name]));

    if (!result.IsValid)
    {
        return result.Status is KapsoWebhookStatus.MissingSignature
                             or KapsoWebhookStatus.InvalidSignature
            ? Results.Unauthorized()
            : Results.BadRequest();
    }

    foreach (var payload in result.Delivery!.Payloads)
    {
        if (payload is KapsoMessagePayload message)
        {
            Console.WriteLine(message.Message?.Kapso?.Content);
        }
    }

    return Results.Ok();
});
```

Batched deliveries and single ones read the same way: `Payloads` holds one entry
for an unbatched event and one per entry for a batch.

An event this version does not know arrives as `KapsoUnknownWebhookPayload` with
the raw JSON, so an endpoint keeps working when Kapso ships something new.

Deliveries are at-least-once. Key your processing on
`result.Delivery.IdempotencyKey`.

## Retries and rate limits

Retries are not uniform across HTTP methods, because they cannot safely be:

- **429** means Kapso refused the request before acting on it. Replaying it
  cannot duplicate anything, so it is retried for every method, honouring
  `Retry-After`.
- **5xx and dropped connections** are ambiguous — the request may have been
  processed and only the response lost. Replaying a POST would send the WhatsApp
  message twice, so these are retried only for idempotent methods.

Throttling also does not count towards the circuit breaker: rate limiting is
ordinary backpressure from a healthy service, and treating it as failure would
open the circuit and reject calls Kapso would have served.

Current limits are on the client:

```csharp
if (kapso.RateLimit is { Remaining: < 50 })
{
    // slow down before hitting the 429
}
```

To own the policy yourself, turn ours off and configure the pipeline:

```csharp
builder.Services
    .AddKapso(options => options.Retry.Enabled = false)
    .AddStandardResilienceHandler();
```

## Native AOT and trimming

The package is AOT- and trim-compatible. `samples/Kapso.Samples.Aot` is published
with `PublishAot=true` in CI, so the claim is checked rather than asserted.

## Regenerating from the OpenAPI descriptions

The descriptions are vendored under `specs/` and the generated code is committed,
so a build needs neither the network nor Kiota. Kiota itself is pinned in
`.config/dotnet-tools.json`.

```powershell
pwsh eng/Sync-Specs.ps1          # refresh specs/, report what changed
pwsh eng/Generate-Clients.ps1    # regenerate src/Kapso/Generated
```

Regenerating on a clean checkout reproduces exactly what is committed; CI asserts
that. See [eng/README.md](eng/README.md) for why the WhatsApp description is
split into four before generation — without it, six of its forty-two operations
disappear from the client with no error.

## Contributing

```bash
dotnet build -c Release   # warnings are errors
dotnet test
```

Tests run on [Microsoft.Testing.Platform](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-intro),
selected in `global.json`.

## License

MIT. Not an official Kapso product.
