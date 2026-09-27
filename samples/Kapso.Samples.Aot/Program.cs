using System.Text;

using Kapso;
using Kapso.Pagination;
using Kapso.Webhooks;
using Kapso.Webhooks.Models;

// Touches every hand-written surface so the AOT compiler has to keep it all,
// which is the point: a trim or AOT problem shows up when this is published,
// not in a consumer's app.

using var kapso = new KapsoClient(new KapsoClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("KAPSO_API_KEY") ?? "not-a-real-key",
});

Console.WriteLine($"WhatsApp   -> {KapsoEndpoints.WhatsApp}");
Console.WriteLine($"Platform   -> {KapsoEndpoints.Platform}");
Console.WriteLine($"Rate limit -> {kapso.RateLimit?.Remaining?.ToString() ?? "not observed yet"}");

// Webhook verification and parsing, the part that is not generated.
const string secret = "whsec_sample";
var body = """
    {"message":{"id":"wamid.1","timestamp":"1730092800","type":"text","from":"16315551181",
     "kapso":{"direction":"inbound","content":"Hello"}},
     "conversation":{"id":"conv_1","contact_name":"Sample"},
     "phone_number_id":"123456789012345"}
    """u8.ToArray();

var headers = new KapsoWebhookHeaders
{
    EventName = KapsoWebhookEventNames.WhatsApp.MessageReceived,
    Signature = KapsoWebhookSignature.Compute(body, secret),
    IdempotencyKey = Guid.NewGuid().ToString(),
};

var result = new KapsoWebhookReader(secret).Read(body, headers);
Console.WriteLine($"Webhook    -> {result.Status}");

if (result.Delivery?.Payload is KapsoMessagePayload message)
{
    Console.WriteLine($"  from      {message.Message?.From ?? message.Message?.FromBusinessScopedUserId}");
    Console.WriteLine($"  at        {message.Message?.Timestamp:O}");
    Console.WriteLine($"  content   {message.Message?.Kapso?.Content}");
}

// Only enumerated when a key is present; the reference alone is enough to keep
// the pagination code rooted for the AOT compiler.
if (Environment.GetEnvironmentVariable("KAPSO_API_KEY") is not null)
{
    await foreach (var customer in kapso.Platform.Customers.EnumerateAsync())
    {
        Console.WriteLine($"  customer  {customer.Name}");
    }
}

Console.WriteLine("ok");
