# Engineering

Everything that turns Kapso's OpenAPI descriptions into the committed client.

| | |
| --- | --- |
| `Sync-Specs.ps1` | Downloads the descriptions into `specs/` and records their provenance |
| `Kapso.SpecTool` | Splits the WhatsApp description, and fails on Kiota path collisions |
| `Generate-Clients.ps1` | Runs Kiota for all seven clients |

```powershell
pwsh eng/Sync-Specs.ps1          # refresh, report drift
pwsh eng/Sync-Specs.ps1 -Check   # report drift without writing (CI)
pwsh eng/Generate-Clients.ps1    # regenerate src/Kapso/Generated
```

## Why there are seven clients

Kapso documents three APIs. The client generates seven.

Three of them are the obvious ones: Platform, Workflows and Agent. Platform and
Workflows share a base address and are the same service, split in Kapso's
reference only to keep it navigable; keeping that split makes the facade read the
way the documentation does.

The other four all come from the WhatsApp description, and that needs explaining.

### Kiota loses operations to path collisions

Kiota models an API as a tree of URI templates, keyed on the *shape* of each
path, with every parameter normalized to a single placeholder. Two routes that
differ only in a parameter's name land on the same node.

Kapso's WhatsApp API mirrors Meta's Graph API, where the first path segment is an
opaque ID whose meaning depends on what it identifies. So these collapse together:

```
/{}        <=  /{media_id} (GET, DELETE)
               /{flow_id} (GET, POST)
               /{phone_number_id} (GET, POST)

/{}/flows  <=  /{business_account_id}/flows (GET, POST)
               /{phone_number_id}/flows (GET, POST)
```

Whichever operations lose the collision are dropped from the generated client.
**Six of the API's forty-two, with no build error** — Kiota logs it and carries on.

`--include-path` does not help: the filter is matched after the same
normalization, so a pattern meant for `/{phone_number_id}/**` matches every
`/{id}/**` route.

### The split

`Kapso.SpecTool split` writes four documents, grouped by the resource the leading
ID denotes — `phone`, `waba`, `flow`, `media`. Each is collision-free, so all
forty-two operations survive, and the facade ends up clearer than the flat
original:

```csharp
kapso.WhatsApp.PhoneNumbers["…"].Messages
kapso.WhatsApp.BusinessAccounts["…"].Message_templates
kapso.WhatsApp.Flows["…"].Publish
kapso.WhatsApp.Media["…"]
```

The split is refused rather than silently wrong if it would lose anything: a path
matching no group, a group that still collides internally, or an operation count
that does not add up all fail the run. If Kapso adds a new resource root, that is
the signal to add a prefix to `WhatsAppGroups` in `Kapso.SpecTool/Program.cs`.

`Kapso.SpecTool check` runs the same collision analysis over every description
before generation, so a collision introduced upstream fails the build instead of
quietly shrinking the client.

## Which Platform description

Kapso publishes two, and neither contains the other:

- `openapi-platform.yaml` — 71 paths. Feeds the published documentation.
- `openapi-platform-new.yaml` — 52 paths, 11 of them unique (`/db/{table}`,
  `/integrations/*`). Absent from the documentation navigation.

The endpoints unique to `-new` do exist (they answer 401, where an unknown route
answers 404), but the two disagree on 16 shared paths and 17 shared schemas.
Merging would mean guessing which definition is current, and guessing wrong
produces wrong types that still compile — worse, for a typed client, than a
missing endpoint.

So only the documented description is generated. `-new` is tracked by hash in
`specs/provenance.json` so its changes are noticed; when Kapso documents it, the
switch is one line in `Sync-Specs.ps1`.

## Why the generated code is committed

Builds need neither the network nor Kiota, CI works offline, and each
regeneration is a reviewable diff — which is where a breaking change in Kapso's
contract becomes visible. Kiota's version is pinned in
`.config/dotnet-tools.json` because different versions emit different code.
