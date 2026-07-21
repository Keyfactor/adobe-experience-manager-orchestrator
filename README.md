# Adobe Experience Manager (Cloud Manager) Orchestrator

A Keyfactor Universal Orchestrator extension that manages customer-managed (OV/EV) TLS/SSL
certificates on **Adobe Experience Manager as a Cloud Service** through the **Adobe Cloud Manager API**.

Supported job types: **Inventory**, **Management (Add / Remove)**, **Discovery**.

> Scaffold / prototype. The endpoints and payloads are modeled directly on the Cloud Manager
> OpenAPI spec (`api.yaml`). See open items in [`docs/DESIGN.md` §8](#design) before production use.
> `docs/DESIGN.md` is intentionally not committed here; keep it wherever you track design notes.

## Store type short name

`AEMCM` — see `integration-manifest.json` for the full Certificate Store Type definition
(create it in Command with `kfutil store-types create` or by hand).

| Store field | Meaning |
|---|---|
| Client Machine | Cloud Manager base URL (`https://cloudmanager.adobe.io`) |
| Store Path | Numeric Cloud Manager **programId** (one store = one program) |
| Server Username | IMS Client ID (API key) |
| Server Password | IMS Client Secret |
| `ImsOrgId` (custom field, secret) | Adobe IMS Org ID → `x-gw-ims-org-id` |
| `ImsTokenUrl` / `ImsScopes` (custom fields) | IMS OAuth Server-to-Server token endpoint / scopes |

## Key platform constraints (drive the logic)

- Max **70** certificates per program (incl. Adobe-managed DV and expired) → Management prefers
  **update over add** and reports all certs in Inventory.
- Max **100 SANs** per certificate.
- Customer-managed certs must be **OV/EV**, PEM, private key **PKCS#8 unencrypted**, RSA-2048 or
  EC secp256r1/secp384r1.
- The leaf certificate must **not** appear in the chain field (handled by `PfxSplitter`).
- A cert with active domain mappings cannot be deleted (handled by Remove's safe-delete check).

## Project layout

Follows the standard Keyfactor UO extension layout (cf. `azurekeyvault-orchestrator`): a `Jobs/`
folder with an abstract base class the jobs inherit, plus the client, properties, and helpers at
the project root.

```
AEMCM.Orchestrator/          class library (net6.0;net8.0;net10.0)
  Jobs/
    AemcmJob.cs              abstract base : IOrchestratorJobExtension (InitializeStore, shared state)
    Inventory.cs             : AemcmJob<Inventory>,  IInventoryJobExtension
    Management.cs            : AemcmJob<Management>,  IManagementJobExtension
    Discovery.cs             : AemcmJob<Discovery>,   IDiscoveryJobExtension
  Client/                    IMS auth + Cloud Manager client (+ interfaces) + API models
  Logic/                     PfxSplitter, CertMatcher, BudgetManager (pure, unit-tested)
  AemcmProperties.cs         resolved connection/config + store custom fields
  Constants.cs               store type name + defaults
  JobAttribute.cs            [Job(...)] marker + JobTypes
  PamUtilities.cs            PAM secret resolution
  manifest.json              capability → class registration
AEMCM.Orchestrator.Tests/    xUnit tests for the Logic + Discovery parsing
integration-manifest.json    store-type definition (also copied to build output)
api.yaml                     Cloud Manager OpenAPI spec (reference)
```

Jobs are constructed with an `IPAMSecretResolver` (injected by the orchestrator) and call
`InitializeStore(config)` on the base class, which resolves credentials, parses the store path
(`programId`) and custom fields, and builds the IMS auth + Cloud Manager clients.

## Building

Keyfactor packages are on GitHub Packages. Configure the feed credential once (a GitHub PAT with
`read:packages`), then restore/build/test:

```bash
dotnet nuget add source https://nuget.pkg.github.com/Keyfactor/index.json \
  --name keyfactor-github --username <you> --password <PAT> --store-password-in-clear-text

dotnet restore
dotnet build -c Release
dotnet test
```

> Pin `Keyfactor.Orchestrators.IOrchestratorJobExtensions` / `Keyfactor.Logging` in
> `AEMCM.Orchestrator.csproj` to the versions matching your target Universal Orchestrator release.

## Deploying

Copy the build output (DLLs + `manifest.json` + `integration-manifest.json` + dependencies) into a
subfolder of the orchestrator's `extensions/` directory and restart the orchestrator service.

<a name="design"></a>
## Design

Full design — API mapping, auth, job flows, SAN-consolidation decision tree, error handling, and
open questions — lives in `docs/DESIGN.md`.
