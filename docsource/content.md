## Overview

The Adobe Experience Manager (Cloud Manager) Universal Orchestrator extension enables Keyfactor Command to
remotely manage customer-managed (OV/EV) TLS/SSL certificates on **Adobe Experience Manager as a Cloud Service**
(AEMaaCS) through the **Adobe Cloud Manager API**. These certificates secure custom domains served by AEMaaCS.
The extension supports inventory, enrollment (add), removal, and discovery.

In Cloud Manager, certificates are scoped to a **program**, so a certificate store of the `AEMCM` store type
represents a single Cloud Manager program (the store's Store Path is the numeric `programId`), and every
certificate in that program is managed through that one store.

Two platform caveats are worth calling out up front: Cloud Manager enforces a hard limit of **70 installed
certificates per program** (including Adobe-managed DV certificates and expired certificates) and up to
**100 SANs per certificate**; and Adobe-managed (DV) certificates are **read-only** to this extension, which
manages only customer-managed (OV/EV) certificates. Because of the 70-certificate limit, the extension prefers
updating an existing certificate over creating a new one.

## Requirements

### Configure Adobe Cloud Manager API access

The extension authenticates to the Cloud Manager API with an Adobe IMS **OAuth Server-to-Server** credential
(Adobe has deprecated JWT authentication; it is not used).

1. In the [Adobe Developer Console](https://developer.adobe.com/console), create (or open) a project and add the
   **Cloud Manager API**, choosing the **OAuth Server-to-Server** credential type.
2. Assign the credential a product profile / role with permission to manage Cloud Manager SSL certificates — the
   **Business Owner** or **Deployment Manager** role.
3. Record the **Client ID**, **Client Secret**, and **IMS Organization ID**; these are entered on the certificate
   store (or discovery job) in Keyfactor Command.

> :warning: A credential without the **Business Owner** or **Deployment Manager** role can authenticate but cannot
> add, update, or delete certificates.

### Endpoint access / firewall

The orchestrator host needs outbound access to:

- The Keyfactor Command instance
- `ims-na1.adobelogin.com` — Adobe IMS token endpoint (to obtain the bearer access token)
- `cloudmanager.adobe.io` — the Cloud Manager API (inventory, add, remove, and discovery operations)

### Certificate requirements

Customer-managed certificates must meet Cloud Manager's requirements. The extension performs best-effort
client-side validation and Cloud Manager enforces the rest server-side:

- **OV or EV** certificates from a trusted CA. DV and self-signed certificates are not supported for
  customer-managed upload.
- Private key in **PKCS#8**, unencrypted. Supported key types: **RSA-2048**, or Elliptic Curve
  **secp256r1 (prime256v1)** / **secp384r1**. RSA-3072/4096 are not supported by Cloud Manager.
- Up to **100 SANs** per certificate.

The extension automatically splits the PKCS#12 supplied by Command into the leaf certificate, the unencrypted
PKCS#8 private key, and the intermediate chain **with the leaf excluded** (Cloud Manager rejects uploads that
include the leaf in the chain).

## Post Installation

After installing the extension DLLs on the orchestrator, create the `AEMCM` certificate store type in Keyfactor
Command — with [kfutil](https://github.com/Keyfactor/kfutil) (`kfutil store-types create AEMCM`) or manually from
`integration-manifest.json` — before defining any certificate stores.
