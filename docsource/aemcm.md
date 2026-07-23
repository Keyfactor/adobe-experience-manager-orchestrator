## Overview

The `AEMCM` certificate store type represents a single **Adobe Cloud Manager program**. When you define a
certificate store of this type, its Store Path is the numeric `programId`, and the store manages every
customer-managed (OV/EV) SSL certificate in that program through the Cloud Manager API.

The certificate **alias is the Adobe certificate name**. It is supplied at enrollment, stored as the certificate's
name in Cloud Manager, and reported back by inventory, so aliases round-trip between enrollment and inventory. Name
uniqueness within a program is enforced on enrollment.

Caveats specific to this store type:

- A program is limited to **70 installed certificates** (including Adobe-managed DV and expired certificates) and
  each certificate to **100 SANs**. To conserve this budget the extension updates an existing certificate when it
  can, rather than creating duplicates.
- Adobe-managed (DV) certificates are **read-only**; the extension will not modify or remove them.

## Requirements

Before creating certificate stores of this type:

1. Configure an Adobe IMS **OAuth Server-to-Server** credential with the **Business Owner** or **Deployment
   Manager** role, as described in the extension-wide Requirements section.
2. Record the credential's **Client ID**, **Client Secret**, and **IMS Organization ID**.
3. Identify the Cloud Manager **`programId`** for each program you intend to manage (a Discovery job can find these
   for you).

Credentials map to the store fields as follows: **Server Username** = IMS Client ID, **Server Password** = IMS
Client Secret, and the **IMS Organization ID** custom field = your IMS Org ID.

## Discovery Job Configuration

Discovery enumerates the Cloud Manager programs your credential can access and returns each as a discoverable
store path (a `programId`).

Because a Discovery job has no certificate store, the store's custom fields (including the IMS Org ID) are not
available on the discovery form. Provide the Org ID(s) through the standard discovery fields instead:

- **Client Machine**: the Cloud Manager base URL, `https://cloudmanager.adobe.io`.
- **Server Username / Password**: the IMS Client ID / Client Secret.
- **Directories to Search**: one or more **comma-separated IMS Organization IDs**. This field is required — with no
  value the job cannot determine which organization's tenants and programs to enumerate.

For each Org ID, the job lists the organization's tenants (`GET /api/tenants`) and then each tenant's programs
(`GET /api/tenant/{tenantId}/programs`), returning every `programId`. Approve a result to create an `AEMCM`
certificate store for that program.

## Certificate Operations

### Inventory

Inventory pages through all certificates in the program and reports every one — including Adobe-managed (DV) and
expired certificates — so the 70-certificate limit is visible. Certificate type, status, common name, SANs, and
expiration are surfaced as entry parameters. DV certificates are reported read-only.

### Add / Enrollment

Provide an alias; it becomes the Adobe certificate name. Enrolling a name that already exists in the program
**fails unless _Overwrite_ is enabled**, in which case the matching certificate is updated in place. To conserve
the 70-certificate budget, an incoming certificate that matches an existing one by alias or by an identical SAN set
updates that certificate rather than creating a duplicate. If the program is at the 70-certificate limit and no
existing certificate matches, the job fails with guidance to remove expired or unused certificates. Key type
(RSA-2048 or EC secp256r1/secp384r1) and the 100-SAN limit are validated before upload.

### Remove

Removing a certificate deletes it from the program. If the certificate is still referenced by one or more domain
mappings, the job fails and lists the offending mapping(s) — remove the mapping(s) first, then retry, and run the
pipeline afterward to fully undeploy. Adobe-managed (DV) certificates cannot be removed.

## Domain Mappings and Certificate Bindings

In Cloud Manager, a certificate only serves live traffic once a **domain mapping** (CDN configuration)
associates a domain with it. This extension manages **certificates only** — it does not create, update, or
delete domain mappings. Bindings are managed in Cloud Manager (or through the Cloud Manager domain-mapping APIs)
outside of Keyfactor. This has several implications worth understanding:

- **Enrollment installs a certificate; it does not bind it.** Adding a certificate uploads it to the program and
  makes it available, but does not attach it to any domain. Until a domain mapping pointing at the certificate is
  created in Cloud Manager, the certificate is installed but not serving traffic.

- **Renew with _Overwrite_ to preserve existing bindings.** Renewing or replacing a certificate with _Overwrite_
  enabled updates it **in place** — the Cloud Manager certificate id does not change — so any existing domain
  mappings continue to point at the renewed certificate automatically, with no re-mapping required. This is the
  recommended renewal workflow.

- **A brand-new certificate is not automatically adopted by existing domains.** If you enroll a *new* certificate
  (a new alias / name) instead of overwriting, existing domain mappings continue to reference the previous
  certificate. Re-point the mapping(s) to the new certificate in Cloud Manager before retiring the old one.
  (Cloud Manager's own behavior is that when multiple SAN certificates cover the same domain, the most recently
  updated/deployed certificate is served for that domain.)

- **A bound certificate cannot be removed.** A Remove job fails if the target certificate is still referenced by
  one or more domain mappings; the failure message lists the mapping(s). Remove or re-point those mappings in
  Cloud Manager, then re-run the Remove job. Certificates with no domain mappings delete cleanly (run the pipeline
  afterward to fully undeploy).

> Managing certificate-to-domain bindings from Keyfactor is intentionally out of scope for this release. The Cloud
> Manager domain-mapping APIs support full create/update/delete, so this could be added later (for example, via
> entry parameters) if it becomes a requirement.

## Certificate Enrollment Alias Requirements

The alias is the Adobe certificate name and must be unique within the program. Use **Overwrite** to renew or
replace an existing certificate (matched by that name). Certificates created outside Keyfactor that happen to share
a name are surfaced during inventory as `name (id)` so aliases remain unique.
