# Chirograph

Chirograph lets companies issue employment documents (experience and relieving letters) that anyone can verify
instantly. Verification shows the letter is genuine, unaltered by even a single byte, and still valid, by matching it
against the issuer's own record.

1. An HR user at a **verified company** uploads a letter as a PDF and enters its key fields.
2. Chirograph stamps a **QR code, verification ID and "Verify at" link** onto every page. It stores the **SHA-256** of
   the final stamped file along with the fields, and emails the stamped PDF to the employee.
3. A verifier (say, a new employer) scans the QR code or opens the link and sees who issued the letter, from which
   **verified domain**, the key fields, and whether it is **Valid** or **Revoked**.
4. The verifier can upload the PDF they received. If a single byte differs from the issued file, the check fails.

Built with ASP.NET Core 10 (Razor Pages), EF Core 10 (SQLite for development, PostgreSQL for production), PDFsharp and
QRCoder.

---

## Contents

- [Quick start](#quick-start)
- [Demo script](#demo-script)
- [Running the tests](#running-the-tests)
- [Architecture](#architecture)
- [Design decisions](#design-decisions)
- [Security and privacy](#security-and-privacy)
- [Configuration and secrets](#configuration-and-secrets)
- [Using PostgreSQL](#using-postgresql)
- [Going to production](#going-to-production)
- [Known limitations and next steps](#known-limitations-and-next-steps)
- [Third-party components](#third-party-components)

---

## Quick start

Prerequisites: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Nothing else: development uses
SQLite, stores files locally and writes emails to a folder you can read in the app.

```bash
dotnet dev-certs https --trust          # once: sign-in cookies are HTTPS-only
dotnet run --project src/Chirograph.Web
```

Open **https://localhost:7228**. In development:

- the database is created and migrated automatically (`src/Chirograph.Web/App_Data/chirograph.db`);
- every email appears at **https://localhost:7228/dev/mailbox**, with clickable links and attachments;
- a ready-made letter for testing is at **https://localhost:7228/dev/sample-letter.pdf**.

`App_Data/` is git-ignored. Delete it to start over.

## Demo script

**Create org → verify domain → issue letter → verify it → tamper with the PDF → watch it fail.** The whole script
takes about five minutes. It uses the domain `acme.test`, which is safe because nothing leaves your machine.

1. **Register the organization.** Click **Register your company**. Enter `Acme Technologies`, domain `acme.test`, email
   `priya@acme.test` and click **Continue**. You'll see *Check your inbox*.

2. **Confirm your email.** Open the [dev mailbox](https://localhost:7228/dev/mailbox), open *Confirm your email to set
   up Acme Technologies…*, click the link, then **Continue**. You're signed in on the *Domain verification* page.
   Issuing is locked until the domain is proven.

3. **Verify the domain.** Under *Option 2*, pick `admin@acme.test` and click **Send confirmation email**. In the dev
   mailbox, open *Confirm that Acme Technologies may issue documents for acme.test*, follow the link and click
   **Confirm domain**. You'll see *Domain confirmed*.
   *(With a real domain you can use Option 1 instead: add the TXT record shown and click **Check DNS now**.)*

4. **Issue a letter.** Go to **Dashboard → Issue a document**. Download the sample letter using the link under the
   file field and upload it. Choose *Experience letter* and fill in `Anita Desai`, `Associate Engineer`,
   `anita.desai@example.org`, `2019-06-01` and `2024-03-31`. Click **Stamp and issue**. On the document page, click
   **Download stamped PDF** and save it as `stamped.pdf`. Open it and you'll see the QR code and ID in the
   bottom-right corner.

5. **Verify it.** Open the verification link shown on the document page, or scan the QR code (see the tip below).
   You'll see *Valid record from Acme Technologies*, the verified domain **acme.test** and the letter's details.
   Upload `stamped.pdf`, optionally type `Globex Background Checks` as your organization, and click **Check file**.
   The result is **Genuine and unaltered**. You can compare the fingerprint yourself:
   `sha256sum stamped.pdf` (Linux/macOS) or `Get-FileHash stamped.pdf` (PowerShell).

6. **Tamper with the PDF and watch it fail.**

   ```bash
   dotnet run scripts/tamper.cs -- stamped.pdf tampered.pdf
   ```

   This changes a single byte. `tampered.pdf` opens and looks identical, but uploading it gives
   **This file does not match the issued document**. For a convincing forgery, change the designation:

   ```bash
   dotnet run scripts/tamper.cs -- stamped.pdf forged.pdf --replace Associate Principal
   ```

   `forged.pdf` now reads *Principal Engineer* and still fails.

7. **Revoke it.** Back on the document page, enter a reason and click **Revoke document**. Reload the verification
   link: it now says **Revoked on \<date\>**. The reason is not shown publicly.

8. **See it as the employee.** In the dev mailbox, open *Your experience letter from Acme Technologies* (the PDF is
   attached). Follow the *See your documents* link and click **Continue**. *My documents* shows the letter, why it
   was revoked, and every verification, including the one by *Globex Background Checks*.

> **Scanning the QR code with a phone:** the QR code encodes `Chirograph:PublicBaseUrl`, which is
> `https://localhost:7228` in development and so only works on your own machine. To test with a phone on the same
> network, run with that URL set to your machine's address, then issue a new letter:
> `dotnet run --project src/Chirograph.Web -- --urls "https://0.0.0.0:7228" --Chirograph:PublicBaseUrl=https://192.168.1.20:7228`.
> The phone will warn about the development certificate; accept it to continue.

## Running the tests

```bash
dotnet test
```

This runs over 300 tests: domain, application and infrastructure unit tests plus integration tests that host the
real app. The tests run on xUnit v3 with the Microsoft Testing Platform (enabled in `global.json`). Highlights:

| What | Where |
|---|---|
| **One changed byte fails verification**, over HTTP through the public page, at the first, middle, last and random offsets, plus a visible text forgery | `tests/Chirograph.IntegrationTests/TamperDetectionTests.cs` |
| The whole demo script, driven through the real forms | `tests/Chirograph.IntegrationTests/EndToEndTests.cs` |
| Hashing: NIST SHA-256 vectors, every single-bit flip detected, stream = buffer | `tests/Chirograph.UnitTests/Domain/DocumentFingerprintTests.cs` |
| Stamping: the QR code is rendered with PDFium and decoded with ZXing on A4, Letter, landscape, rotated and cropped pages; ID text, link and original content preserved; encrypted, signed and corrupt PDFs rejected | `tests/Chirograph.UnitTests/Infrastructure/PdfSharpStamperTests.cs` |
| Revocation rules and the verification result for every combination | `tests/Chirograph.UnitTests/Domain/IssuedDocumentTests.cs`, `VerificationOutcomeTests.cs`, `Application/RevocationTests.cs` |
| Access control, privacy headers, non-storage of checked files, cookie flags, rate limits | `tests/Chirograph.IntegrationTests/AccessControlTests.cs`, `PrivacyAndSecurityTests.cs`, `RateLimitingTests.cs` |
| Clean-architecture dependency rule | `tests/Chirograph.UnitTests/Architecture/LayeringTests.cs` |

**PostgreSQL.** The persistence contract and an end-to-end application flow also run against a real PostgreSQL
server when you set `CHIROGRAPH_TEST_POSTGRES` to a connection string for a role that can create databases. Each test
uses a throw-away database. Without the variable, these tests are skipped.

```bash
CHIROGRAPH_TEST_POSTGRES="Host=localhost;Username=chirograph;Password=<password>" dotnet test
```

## Architecture

```
src/
  Chirograph.Domain/          Entities, value objects and rules. No dependencies at all.
  Chirograph.Application/     Use cases (sign-up, domain verification, team, issuing, revocation, verification,
                              employee portal) and the ports they need: IAppDbContext, IFileStore, IEmailSender,
                              IPdfStamper, IDnsTxtResolver.
  Chirograph.Infrastructure/  EF Core (SQLite + PostgreSQL migration sets), PDFsharp/QRCoder stamper, local-disk file
                              store, SMTP and dev-outbox email, DnsClient TXT lookups.
  Chirograph.Web/             Razor Pages, cookie auth, rate limiting, security headers. Composition root.
tests/
  Chirograph.UnitTests/       Domain, application (over a migrated SQLite database) and infrastructure tests.
  Chirograph.IntegrationTests/ The real app hosted with WebApplicationFactory, driven through its forms.
scripts/tamper.cs             Demo helper (a .NET 10 file-based app).
```

Dependencies point inwards only: Web → Infrastructure → Application → Domain. `LayeringTests` fails the build if that
changes.

**Data model.** These five tables hold everything Chirograph stores:

| Table | Holds |
|---|---|
| `Organizations` | Display name, domain (lower-case ASCII; international names as punycode), status, DNS challenge, how and when the domain was verified. A filtered unique index allows **one verified organization per domain**. |
| `OrganizationMembers` | Email address on the organization's domain, role (Admin/Issuer), status. No names, phone numbers or passwords. |
| `Documents` | Verification ID, issuer, type, employee name, employee email, designation, employment dates, issue time, SHA-256 of the stamped file, file size, storage key, and revocation time, member and reason. |
| `VerificationEvents` | Time, whether a file was checked and matched, the status shown, and an optional self-declared verifier name. No IP addresses or device details. |
| `MagicLinkTokens` | SHA-256 of each emailed link's token, its purpose, recipient, expiry and use. The token itself is never stored. |

Employees are not a table: they are identified by the email address recorded on their documents.

## Design decisions

These were agreed before building. Each is the default that was proposed:

1. **Proving the domain by email goes only to administrative mailboxes** (`admin@`, `administrator@`, `hostmaster@`,
   `postmaster@`, `webmaster@`). Certificate authorities accept the same set. If any address on the domain counted,
   any employee could register the company. The alternative is a DNS TXT record at `_chirograph.<domain>` (or at the
   domain itself).
2. **The stamp is overlaid in the bottom-right margin of every page.** The page size stays the same, and the QR code is
   vector-drawn, so it prints and scans crisply. It is placed upright as the page is displayed, respecting crop boxes
   and rotation.
3. **The stamped PDF is kept** so the employee can re-download it and HR can resend it. It is never served on the
   public page. The original upload is never stored.
4. **"Who verified" is an optional, self-declared organization name** entered with the file check. It is shown to the
   employee and labelled as self-declared.
5. **HR sign-in is passwordless**: single-use emailed links, with no passwords to store or leak.

Other choices worth knowing:

- **Verification IDs** are 25 characters of Crockford Base32 from a cryptographic random generator: 125 bits, so they
  can't be guessed or enumerated. They're printed as `XXXXX-XXXXX-XXXXX-XXXXX-XXXXX`, and typing is forgiving about
  case, hyphens, and the letters O/I/L.
- **Links and QR codes are built only from `Chirograph:PublicBaseUrl`**, never from the request's Host header, so a
  crafted request cannot get an attacker's address stamped onto a letter.
- **Emailed links are previewed on GET and used on POST**, so an email security scanner that opens links cannot use
  them up. Every link works once and expires (20 minutes for sign-in, 24 hours for sign-up and domain confirmation, 72 hours
  for invitations and the link in the issue email).
- **The public page's view model has no field** for the employee's email or the revocation reason, so they cannot leak
  there by accident. A test enforces this.
- **Rejected uploads:** password-protected, edit-restricted and digitally signed PDFs are refused with an explanation.
  Stamping a signed PDF would silently invalidate its signature.
- **Timestamps are stored with microsecond precision** on both databases, so values are identical after a round trip.
  Running the tests against real PostgreSQL is what uncovered the mismatch this fixes.

## Security and privacy

The design follows the purpose-limitation, minimisation and transparency principles of India's **Digital Personal Data
Protection Act, 2023**:

- **Purpose.** Data is kept only so letters can be verified. The privacy page, the issue email and the verification
  page say so.
- **Minimisation.** Only the fields listed above are stored. Verifiers' uploads are hashed in memory and never stored.
  No IP addresses or device details are stored; rate limiting uses the client IP in memory only.
- **Need-to-know.** The public page, available only with the link or QR code, shows the issuer, verified domain, letter
  type, employee name, role, dates, issue date and status. It never shows the employee's email or a revocation reason.
  Verification pages are `noindex` and `no-store`.
- **Transparency for the employee.** Employees see every letter issued to them, from any employer, with every
  verification (time, what was checked, the result, and who, if the verifier said). They also see the reason for any
  revocation.
- **Web hardening.**
  - Session cookie: `__Host-` prefix, Secure, HttpOnly and SameSite=Lax.
  - Strict Content-Security-Policy with no inline script.
  - `Referrer-Policy: no-referrer`, so verification IDs and link tokens don't leak to other sites.
  - Anti-forgery tokens on every form.
  - Staff membership re-checked on every request, so a removed member loses access immediately.
- **Rate limits** (built into ASP.NET Core; configurable):
  - page views: 60 per IP per minute;
  - file checks: 10 per IP per 10 minutes;
  - requests that send email: 5 per IP per 15 minutes;
  - issuing: 60 per member per hour.

## Configuration and secrets

Settings live in `src/Chirograph.Web/appsettings*.json`. **Only non-secret defaults are committed.** Put secrets in
user-secrets for development or environment variables in production. Environment variables use `__` as the separator,
e.g. `Email__Smtp__Password`.

| Setting | Default | Notes |
|---|---|---|
| `Chirograph:PublicBaseUrl` | `https://localhost:7228` | The public address printed on every letter. **Set it before issuing real letters; it is permanent.** |
| `Chirograph:MaxUploadBytes` | `10485760` | Largest PDF accepted, for issuing or checking. |
| `Database:Provider` | `Sqlite` | `Sqlite` or `Postgres`. |
| `ConnectionStrings:Chirograph` | `Data Source=App_Data/chirograph.db` | **Secret** for PostgreSQL. |
| `Database:ApplyMigrationsOnStartup` | `false` (`true` in Development) | |
| `Storage:RootPath` | `App_Data/files` | Where stamped PDFs are stored. |
| `Email:Delivery` | `Outbox` | `Outbox` (write `.eml` files, for development) or `Smtp`. |
| `Email:FromAddress`, `Email:FromName` | `no-reply@chirograph.local`, `Chirograph` | |
| `Email:Smtp:Host`, `Port`, `Username`, `Security` | —, `587`, —, `StartTls` | `Security`: `Auto`, `StartTls`, `SslOnConnect` or `None`. |
| `Email:Smtp:Password` | — | **Secret.** |
| `Dns:NameServers` | system resolvers | Optional list of resolver IPs used for TXT checks. |
| `RateLimits:*` | see above | `VerificationViewsPerMinute`, `FileChecksPerTenMinutes`, `EmailRequestsPerFifteenMinutes`, `IssuesPerHour`. |
| `DataProtection:KeysPath` | `App_Data/keys` | Keys that protect cookies; keep them on persistent storage. |
| `ForwardedHeaders:Enabled` | `false` | Enable only behind a reverse proxy that is the sole way to reach the app. |

```bash
# Development secrets (stored outside the repository)
dotnet user-secrets set "Email:Smtp:Password" "<password>" --project src/Chirograph.Web
dotnet user-secrets set "ConnectionStrings:Chirograph" "Host=localhost;Database=chirograph;Username=chirograph;Password=<password>" --project src/Chirograph.Web

# Production: environment variables
export ConnectionStrings__Chirograph="Host=db;Database=chirograph;Username=chirograph;Password=<password>"
export Email__Smtp__Password="<password>"
```

## Using PostgreSQL

```bash
export Database__Provider=Postgres
export ConnectionStrings__Chirograph="Host=localhost;Database=chirograph;Username=chirograph;Password=<password>"
dotnet run --project src/Chirograph.Web      # Development applies migrations automatically
```

Each provider has its own checked-in migrations: `src/Chirograph.Infrastructure/Persistence/Migrations/{Sqlite,Postgres}`.
To produce an idempotent SQL script for a production database:

```bash
dotnet tool restore
dotnet ef migrations script --idempotent --context PostgresChirographDbContext \
  --project src/Chirograph.Infrastructure --startup-project src/Chirograph.Infrastructure -o chirograph-postgres.sql
```

When you change the model, add a migration for **both** providers (a test fails if either is out of date):

```bash
dotnet ef migrations add <Name> --context SqliteChirographDbContext \
  --project src/Chirograph.Infrastructure --startup-project src/Chirograph.Infrastructure --output-dir Persistence/Migrations/Sqlite
dotnet ef migrations add <Name> --context PostgresChirographDbContext \
  --project src/Chirograph.Infrastructure --startup-project src/Chirograph.Infrastructure --output-dir Persistence/Migrations/Postgres
```

## Going to production

- Set `Chirograph:PublicBaseUrl` to the final public HTTPS address before issuing anything: it is printed on every
  letter.
- Configure SMTP (`Email:Delivery=Smtp`), use PostgreSQL, and apply migrations.
- Keep `Storage:RootPath` and `DataProtection:KeysPath` on persistent storage. For more than one instance, implement
  `IFileStore` over object storage, share the data-protection keys, and use a distributed rate limiter: the built-in
  one is per instance.
- Restrict `AllowedHosts` to your host names and serve over HTTPS; HSTS is on outside Development.

## Known limitations and next steps

- **Stamp placement.** The stamp is overlaid in the bottom-right corner and can overlap a letterhead's printed footer.
  An alternative is to add a band below each page (never overlaps, but changes the page size).
- **Retention and erasure.** There is no workflow yet for deleting documents at the end of a retention period or on an
  erasure request. Issuers can revoke.
- **Sign-in security** rests on mailbox security (passwordless). Adding a second factor for admins is a natural next
  step.
- **Timing.** Response times on the sign-in forms may hint whether an address is known. Rate limits reduce the risk;
  sending email in the background would remove it.
- **Out of scope for this MVP:** payslips, HR-system integrations, digital signatures with issuer keys,
  background-verification firm APIs and billing.

## Third-party components

**Runtime:**
- ASP.NET Core and EF Core (MIT)
- Npgsql (PostgreSQL License)
- PDFsharp (MIT)
- QRCoder (MIT)
- MailKit/MimeKit (MIT)
- DnsClient (Apache-2.0)
- Bootstrap, jQuery and jQuery Validation (MIT)
- Liberation Sans font (SIL Open Font License 1.1; see `src/Chirograph.Infrastructure/Pdf/Fonts/OFL.txt`), embedded
  so stamps render identically on every OS

**Tests only:**
- xUnit v3 (Apache-2.0)
- PdfPig (Apache-2.0)
- Docnet.Core (MIT), with PDFium
- ZXing.Net (Apache-2.0)
- AngleSharp (MIT)
