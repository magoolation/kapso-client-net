# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2026-09-27

Backward compatible with 0.2.0, verified by package validation against the
published package.

### Added

- `UploadAsync` and `UploadFileAsync` for uploading WhatsApp media. The generated
  call could not be used from outside this package at all: it takes a Kiota
  `MultipartBody`, a `MultipartBody` needs the request adapter to serialize
  itself, and Kiota declares `BaseRequestBuilder.RequestAdapter` as protected.
  The helper also sets the required `messaging_product` part, whose absence is
  what makes an otherwise correct upload fail.

### Repository

Not shipped in the package, but part of this release:

- `Kapso.Samples.SendMedia` uploads and sends an image, an Opus voice note and a
  PDF. All three were sent against a live number.
- The webhook receiver repoints its own webhook at the current tunnel and reads
  the secret from the API, so no copy of the secret can drift out of sync. Real
  `whatsapp.message.sent` and `.delivered` deliveries were verified end to end.
- `run-with-tunnel.ps1` puts the receiver behind a Cloudflare quick tunnel.

## [0.2.0] - 2026-09-27

Backward compatible with 0.1.0, verified by package validation against the
published package.

### Added

- A failed response is now logged with the body the server sent. Kiota keeps only
  the status code when a body matches no error schema in the OpenAPI description,
  and Kapso's errors frequently do not — a refused send returns
  `{"error":"Active sandbox session required to send messages"}`, which fits none
  of them, so the caller was left with a bare 403 and nothing to act on. Pass an
  `ILoggerFactory` to the constructor, or use `AddKapso` and the application's
  logging is used.
- Constructor overloads taking an `ILoggerFactory`.

### Repository

Not shipped in the package, but part of this release:

- Runnable samples: a webhook receiver that verifies itself with no Kapso
  account, a read-only tour of both APIs, and a message sender.
- `eng/Test-NoSecrets.ps1`, which fails the build if anything secret-shaped is
  committed, and runs in CI.
- Package validation is now pinned to a baseline, so an API break against the
  last published version fails the build.

## [0.1.0] - 2026-09-27

### Added

- Typed clients for the WhatsApp, Platform, Workflows and Agent APIs, generated
  with Kiota from Kapso's official OpenAPI descriptions.
- `KapsoClient` facade, usable directly or through `services.AddKapso(...)`.
- Resilience that distinguishes a refused request from an ambiguous one: `429`
  is retried for every method, `5xx` and dropped connections only for idempotent
  ones, so a retry cannot send a WhatsApp message twice.
- Rate limit state from the latest response on `KapsoClient.RateLimit`.
- `EnumerateAsync` pagination over both of Kapso's paging schemes.
- Webhook signature verification and typed payloads for the documented events.
- Native AOT and trimming support, verified by publishing a sample in CI.

[Unreleased]: https://github.com/magoolation/kapso-client-net/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/magoolation/kapso-client-net/releases/tag/v0.3.0
[0.2.0]: https://github.com/magoolation/kapso-client-net/releases/tag/v0.2.0
[0.1.0]: https://github.com/magoolation/kapso-client-net/releases/tag/v0.1.0
