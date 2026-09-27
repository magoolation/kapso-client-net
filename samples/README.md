# Samples

Four runnable programs. None of them contains a credential, and none can be made
to contain one: `eng/Test-NoSecrets.ps1` fails the build if anything
secret-shaped is ever committed.

| | What it proves | Needs a key |
| --- | --- | --- |
| [Kapso.Samples.WebhookReceiver](Kapso.Samples.WebhookReceiver) | Signature verification and typed dispatch, end to end | no |
| [Kapso.Samples.Quickstart](Kapso.Samples.Quickstart) | Auth, both base addresses, pagination, rate limits — read-only | yes |
| [Kapso.Samples.SendMessage](Kapso.Samples.SendMessage) | Sending a real WhatsApp message | yes |
| [Kapso.Samples.Aot](Kapso.Samples.Aot) | The package works under Native AOT | no |

## Where secrets live

Never in this repository. Use [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets),
which are stored in your user profile. Every sample shares one vault, so the key
is set once from any sample directory:

```bash
cd samples/Kapso.Samples.Quickstart
dotnet user-secrets set "Kapso:ApiKey" "<your key>"
```

Or the environment — `Kapso__ApiKey`, or `KAPSO_API_KEY` as a shorthand. In CI,
a GitHub Actions secret.

Get a key from the Kapso dashboard under **Integrations → API keys**.

## Webhook receiver

The only sample that proves itself with no account at all. It signs a delivery,
posts it to its own endpoint, and checks the outcomes:

```bash
dotnet run --project samples/Kapso.Samples.WebhookReceiver -- --selftest
```

```
PASS  a correctly signed delivery is accepted  (200)
PASS  a delivery signed with the wrong secret is rejected  (401)
PASS  a delivery with no signature is rejected  (401)
PASS  a delivery with a malformed signature is rejected  (401)
PASS  a body altered after signing is rejected  (401)
```

To receive real webhooks, set the secret and run it:

```bash
cd samples/Kapso.Samples.WebhookReceiver
dotnet user-secrets set "Kapso:WebhookSecret" "<the webhook's secret>"
dotnet run
```

Expose it with ngrok or a Cloudflare tunnel and register the HTTPS URL in Kapso.

The one thing to copy from this sample: it reads the **raw request body** before
anything parses it. Model binding hands back a re-serialized object whose bytes
differ from what Kapso signed — different key order, whitespace or unicode
escaping — and every difference fails verification.

## Quickstart

Read-only. Lists phone numbers, walks every customer page, reads a business
profile and recent conversations, then prints the rate limit state.

```bash
cd samples/Kapso.Samples.Quickstart
dotnet user-secrets set "Kapso:ApiKey" "<your key>"
dotnet run
```

Run this first: it prints the phone number IDs that the other samples need.

## Send message

The only sample with consequences. The message arrives on a real device and
Kapso charges for it, so it prints what it is about to do and refuses to send
without `--send`.

```bash
cd samples/Kapso.Samples.SendMessage
dotnet user-secrets set "Kapso:PhoneNumberId" "<from the quickstart output>"
dotnet user-secrets set "Kapso:Recipient"     "<your own number, digits only>"

dotnet run              # shows what it would send, sends nothing
dotnet run -- --send    # actually sends
```

The API key comes from the shared vault, so it is not repeated here.

The recipient is masked in the output, so a full number never reaches your
terminal scrollback or a CI log.
