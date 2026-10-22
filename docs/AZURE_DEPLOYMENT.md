# Deploy MentorPath to Azure

This is the supported MVP deployment path (D-47). Run commands yourself; changing this repository does **not** create Azure resources. Commands use **Bash**, from the repository root, in one terminal session.

## What you will deploy

| Resource | Starting choice | Purpose |
|---|---|---|
| App Service Linux | B1, one instance, classic custom container | .NET 10 / Blazor Interactive Server; HTTPS and WebSockets |
| Container Registry | Basic, RBAC permissions mode | Versioned application images |
| Azure Database for PostgreSQL Flexible Server | PostgreSQL 17, Burstable B1ms, 32 GiB | Application and Identity data |
| Storage account | StorageV2, Standard LRS | Private attachment and Data Protection containers |
| Key Vault | Standard, Azure RBAC | Database connection string and SMTP password |
| App managed identity | System-assigned | Pull image, access blobs and resolve Key Vault references |
| Email Communication Services + Communication Services | Consumption, linked email domain | ACS Email SMTP for password-reset emails; temporary option, retirement September 30, 2028 |

**Email lifecycle warning (checked October 22, 2026):** the owner requested Azure Communication Services Email to keep administration in Azure. Microsoft has since announced that **ACS Email retires September 30, 2028** and recommends against onboarding new solutions. Its FAQ currently permits new email workloads, but says availability can change at any time; the wider ACS onboarding restrictions begin October 23, 2026. Do not assume your subscription can provision it. See the [official retirement guide](https://learn.microsoft.com/azure/communication-services/acs-retirement-and-breaking-changes-guide#acs-email) before creating resources. The steps below support the requested choice only as a **temporary deployment**, with a provider migration required before retirement. A supported SMTP provider can use the same application settings without changing code.

These are **paid** resources, including while the app is idle. Check the [Azure calculator](https://azure.microsoft.com/pricing/calculator/) for your region and set a subscription budget/alert before provisioning. B1/B1ms are a small starting point, not an availability or capacity guarantee; this setup has no database HA or zero-downtime deployment.

There is no AWS dependency. Attachments are not public and have no SAS download links: users download through the existing authorized endpoint. The baseline uses public Azure service endpoints protected by TLS, RBAC and PostgreSQL IP firewall rules, **not** a fully private VNet. For stricter networking, design VNet integration, private endpoints, private DNS and an operator VPN before deployment.

## 1. Prerequisites and names

Install a current [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), .NET 10 SDK, Git, OpenSSL, and PostgreSQL 17 client tools (`psql`, `pg_dump`, `pg_restore`). Docker is needed for local tests. You need subscription resource-creation permissions **and** role-assignment permissions (Owner or User Access Administrator plus Contributor). Contributor alone cannot assign the roles below.

Choose a region with the listed SKUs and quota. Resource names must be globally unique where applicable. Do not run with shell tracing (`set -x`), pipe commands to log collectors, commit environment files, or paste secrets into issue/PR comments.

```bash
az login
az account set --subscription "<subscription-id>"
az account show --query '{subscription:name,id:id}' -o table

LOCATION=westeurope
SUFFIX="<unique-lowercase-letters-and-digits>"  # e.g. your initials + six digits
RG="mentorpath-prod-$SUFFIX"
PLAN="mentorpath-plan-$SUFFIX"
APP="mentorpath-$SUFFIX"
ACR="mpacr$SUFFIX"       # alphanumeric only, total 5-50 characters
STORAGE="mpfiles$SUFFIX" # lowercase alphanumeric, total 3-24 characters
VAULT="mp-kv-$SUFFIX"   # total 3-24 characters
PG="mentorpath-pg-$SUFFIX"
PGADMIN=mentorpathadmin
ATTACHMENTS=mentorpath
KEYS=identity-keys
TAG="$(git rev-parse HEAD)"
read -r -p "Your workstation's public IPv4 address (not a local/VPN address): " OPERATOR_IP

az group create --name "$RG" --location "$LOCATION" --output none
for PROVIDER in Microsoft.Web Microsoft.Storage Microsoft.ContainerRegistry Microsoft.DBforPostgreSQL Microsoft.KeyVault Microsoft.Communication; do
  az provider register --namespace "$PROVIDER" --wait --output none
done
OPERATOR_ID="$(az ad signed-in-user show --query id -o tsv)"
```

The examples assume an interactive human `az login`, not a service principal. For automation, use workload identity federation rather than a long-lived client secret. Save the **non-secret** names above outside the repository so subsequent sessions can reconstruct them. Stop on any failed command; do not continue with an empty ID or unresolved secret.

## 2. Registry, private storage and Key Vault

```bash
az acr create --resource-group "$RG" --name "$ACR" --sku Basic \
  --admin-enabled false --role-assignment-mode rbac --output none
ACR_ID="$(az acr show -g "$RG" -n "$ACR" --query id -o tsv)"
LOGIN_SERVER="$(az acr show -g "$RG" -n "$ACR" --query loginServer -o tsv)"

az storage account create -g "$RG" -n "$STORAGE" -l "$LOCATION" \
  --sku Standard_LRS --kind StorageV2 --https-only true --min-tls-version TLS1_2 \
  --allow-blob-public-access false --allow-shared-key-access false --output none
STORAGE_ID="$(az storage account show -g "$RG" -n "$STORAGE" --query id -o tsv)"
BLOB_SERVICE="$(az storage account show -g "$RG" -n "$STORAGE" --query primaryEndpoints.blob -o tsv)"

az role assignment create --assignee-object-id "$OPERATOR_ID" --assignee-principal-type User \
  --role "Storage Blob Data Contributor" --scope "$STORAGE_ID" --output none
# RBAC can take several minutes to propagate. If the next commands return 403, wait and retry them.
az storage container create --account-name "$STORAGE" --name "$ATTACHMENTS" \
  --auth-mode login --public-access off --output none
az storage container create --account-name "$STORAGE" --name "$KEYS" \
  --auth-mode login --public-access off --output none
az storage account blob-service-properties update -g "$RG" --account-name "$STORAGE" \
  --enable-delete-retention true --delete-retention-days 30 \
  --enable-container-delete-retention true --container-delete-retention-days 30 \
  --enable-versioning false --output none

az keyvault create -g "$RG" -n "$VAULT" -l "$LOCATION" \
  --enable-rbac-authorization true --enable-purge-protection true --output none
VAULT_ID="$(az keyvault show -g "$RG" -n "$VAULT" --query id -o tsv)"
az role assignment create --assignee-object-id "$OPERATOR_ID" --assignee-principal-type User \
  --role "Key Vault Secrets Officer" --scope "$VAULT_ID" --output none
```

The operator can initially access all blobs to provision and maintain the service. Remove or reduce this broad grant after bootstrap if it is not needed; grant temporary, container-scoped access for later CLI work. The app receives only container-scoped grants in step 4. Do not enable anonymous blob access or issue SAS URLs.

The `identity-keys` container stores the shared Identity key ring. Azure encrypts storage at rest, but this setup does **not** additionally encrypt the XML key ring with a Key Vault key. Anyone with read access to this container can decrypt application cookies/tokens. Treat its RBAC and backups as sensitive credentials. Never apply attachment-expiration policies to this container.

## 3. PostgreSQL and secrets

Generate two separate passwords; save both in your organization's password manager. The app uses a separate database owner login, not the server administrator. Hex generation keeps the SQL and connection-string examples safe from quoting ambiguities.

```bash
PGADMIN_PASSWORD="$(openssl rand -hex 24)"
PGAPP_PASSWORD="$(openssl rand -hex 24)"
az postgres flexible-server create -g "$RG" -n "$PG" -l "$LOCATION" \
  --admin-user "$PGADMIN" --admin-password "$PGADMIN_PASSWORD" \
  --version 17 --tier Burstable --sku-name Standard_B1ms --storage-size 32 \
  --backup-retention 14 --public-access "$OPERATOR_IP" --output none
PGHOST="$(az postgres flexible-server show -g "$RG" -n "$PG" --query fullyQualifiedDomainName -o tsv)"

PGPASSWORD="$PGADMIN_PASSWORD" psql \
  "host=$PGHOST dbname=postgres user=$PGADMIN sslmode=verify-full" \
  --set ON_ERROR_STOP=1 <<SQL
CREATE ROLE mentorpath_app LOGIN PASSWORD '$PGAPP_PASSWORD';
GRANT mentorpath_app TO $PGADMIN;
CREATE DATABASE mentorpath OWNER mentorpath_app;
REVOKE CONNECT ON DATABASE mentorpath FROM PUBLIC;
GRANT CONNECT ON DATABASE mentorpath TO mentorpath_app;
REVOKE mentorpath_app FROM $PGADMIN;
SQL

DB_CONNECTION="Host=$PGHOST;Port=5432;Database=mentorpath;Username=mentorpath_app;Password=$PGAPP_PASSWORD;SSL Mode=VerifyFull"
# Wait for Key Vault RBAC propagation if necessary.
az keyvault secret set --vault-name "$VAULT" --name db-connection \
  --value "$DB_CONNECTION" --output none
```

The temporary role membership lets the non-superuser Azure server admin create a database owned by `mentorpath_app` (required by PostgreSQL 17). The server admin is a trusted administrative account; never use it for the web app.

The PostgreSQL clients and .NET host need a current OS CA trust store for `verify-full` / `VerifyFull`. On a TLS error, update the trust store using Azure PostgreSQL's current certificate guidance; do not disable verification or use `Trust Server Certificate=true`.

### 3a. Azure Communication Services Email

This uses the existing SMTP sender: **no new NuGet package or application code is needed**. Mailpit remains local-only. App Service and Blob Storage do not send mail on their own. Follow these Azure Portal steps, checking the lifecycle warning above first:

1. Create an **Email Communication Services** resource in the existing `$RG` resource group (for example `mentorpath-email-<suffix>`). Choose the data geography permitted for your deployment and privacy requirements; do not copy a US geography blindly from an example. Confirm supported geographies in your subscription.
2. In that resource, provision a domain. For the simplest pilot, use **Add a free Azure subdomain** / **Azure-managed domain**: no domain purchase or DNS changes are needed, but email sending is still chargeable. For production branding, add your own domain and complete the verification, SPF and DKIM DNS records shown in the portal. Use the portal-provided values, not guessed DNS records.
3. Create a separate **Communication Services** resource in `$RG` (for example `mentorpath-communications-<suffix>`), with the **same data geography** as the email resource. In its **Email → Domains** section, select **Connect domain** and link the verified domain from step 2. Creating only the email resource is not enough.
4. Open the provisioned domain and copy an actual allowed **MailFrom/sender address**. For an Azure-managed domain, use the address Azure provides; do not invent `no-reply@<your-domain>`. This exact address is `SMTP_FROM` below. SMTP credentials and sender address are different values.
5. In **Microsoft Entra ID → App registrations**, create a dedicated, single-tenant application (for example `mentorpath-email-smtp-<suffix>`). No redirect URI is needed for this server-to-server SMTP flow. Tenant policy may require an administrator to create the registration; Azure subscription Contributor/Owner does not automatically grant Entra application-registration permissions.
6. On the **Communication Services** resource (not the Email Communication Services resource), open **Access control (IAM)**. Assign the application's service principal the documented **Communication and Email Service Owner** role, scoped **only to this resource**, not to the resource group or subscription. This built-in role grants broader communication/email operations than just sending. For tighter permissions, use Microsoft's [documented custom SMTP role](https://learn.microsoft.com/azure/communication-services/quickstarts/email/send-email-smtp/smtp-authentication#assign-a-role-to-the-microsoft-entra-application) instead. Do not assign subscription Owner.
7. In the Entra registration, open **Certificates & secrets → New client secret**. Choose an expiry consistent with your rotation policy and record its expiry date. Copy the secret **Value**, not the secret ID, directly into your password manager. This value becomes `SMTP_PASSWORD`; do not put it in source control, tickets or screenshots.
8. On the **Communication Services** resource, open **SMTP Usernames → Add SMTP Username**, select that Entra application, and create a username (for example `mentorpath-smtp`). Wait until its status is **Ready to use**. Copy the exact username shown by Azure as `SMTP_USER`; it is not necessarily the sender email, application ID or your Azure login.

The protocol settings are `smtp.azurecomm.net`, port **587**, and **STARTTLS enabled** (`Email__Smtp__EnableSsl=true` in this app). Do not use implicit TLS port 465 or port 25. The SMTP password is the Entra application's **client-secret value**, not an ACS connection string, ACS access key or Azure account password. App Service managed identity resolves the secret from Key Vault; it does **not** replace SMTP username/password authentication in the current sender.

```bash
SMTP_HOST=smtp.azurecomm.net
read -r -p "ACS SMTP username (Ready to use): " SMTP_USER
read -r -p "Exact provisioned ACS sender address: " SMTP_FROM
read -r -s -p "SMTP Entra application client-secret VALUE: " SMTP_PASSWORD; printf '\n'
az keyvault secret set --vault-name "$VAULT" --name smtp-password \
  --value "$SMTP_PASSWORD" --output none
```

Continue to step 4; step 5 already wires these values and the Key Vault secret into the app. If ACS provisioning is unavailable, stop this email path rather than disabling TLS or leaving SMTP unconfigured. A different SMTP provider needs its own host, username, secret and verified sender with STARTTLS on port 587.

This configures **password-reset emails for existing mentors and mentees**, not automatic invitation or welcome emails. New mentees still receive a manually shared invitation link and choose their password on acceptance; `create-mentor` still prints a one-time setup link for secure manual delivery. Passwords themselves are never emailed.

Before launch, check the resource's current email quotas, set a budget alert, and test delivery to an address you control. An Azure-managed domain may have restrictive quotas; do not assume custom-domain quotas or immediate quota increases. Review Azure's processing geography, delivery-log retention and privacy terms for account email addresses and reset-link content.

Secrets are passed through shell variables, not literal command history; Azure CLI arguments can still be visible to local administrators/process inspection. Run this on a trusted workstation. Do not use `--debug` on commands containing credentials.

## 4. App Service and managed identity

Create the web app with the public .NET image as a temporary placeholder, then stop it until migrations and settings are ready. Use the **classic custom container** model (`--sitecontainers-app false`), not sidecar/sitecontainers; the latter uses a different port configuration.

```bash
az appservice plan create -g "$RG" -n "$PLAN" --is-linux --sku B1 --number-of-workers 1 --output none
az webapp create -g "$RG" -p "$PLAN" -n "$APP" --sitecontainers-app false \
  --container-image-name mcr.microsoft.com/dotnet/aspnet:10.0 --output none
az webapp stop -g "$RG" -n "$APP"
APP_PRINCIPAL="$(az webapp identity assign -g "$RG" -n "$APP" --query principalId -o tsv)"

az role assignment create --assignee-object-id "$APP_PRINCIPAL" --assignee-principal-type ServicePrincipal \
  --role AcrPull --scope "$ACR_ID" --output none
for CONTAINER in "$ATTACHMENTS" "$KEYS"; do
  az role assignment create --assignee-object-id "$APP_PRINCIPAL" --assignee-principal-type ServicePrincipal \
    --role "Storage Blob Data Contributor" \
    --scope "$STORAGE_ID/blobServices/default/containers/$CONTAINER" --output none
done
az role assignment create --assignee-object-id "$APP_PRINCIPAL" --assignee-principal-type ServicePrincipal \
  --role "Key Vault Secrets User" --scope "$VAULT_ID" --output none
az webapp config set -g "$RG" -n "$APP" \
  --generic-configurations '{"acrUseManagedIdentityCreds":true}' \
  --always-on true --web-sockets-enabled true --http20-enabled true --output none
az webapp update -g "$RG" -n "$APP" --https-only true --output none
az webapp config set -g "$RG" -n "$APP" --min-tls-version 1.2 \
  --generic-configurations '{"healthCheckPath":"/health/ready"}' --output none
PUBLIC_HOST="$(az webapp show -g "$RG" -n "$APP" --query defaultHostName -o tsv)"
PUBLIC_URL="https://$PUBLIC_HOST"

# Permit only possible App Service outbound addresses, never 0.0.0.0 / "all Azure".
OUTBOUND_IPS="$(az webapp show -g "$RG" -n "$APP" --query possibleOutboundIpAddresses -o tsv)"
IFS=',' read -r -a IP_LIST <<< "$OUTBOUND_IPS"
INDEX=0
for IP in "${IP_LIST[@]}"; do
  INDEX=$((INDEX + 1))
  az postgres flexible-server firewall-rule create -g "$RG" -n "$PG" \
    --rule-name "app-outbound-$INDEX" --start-ip-address "$IP" --end-ip-address "$IP" --output none
done
```

Recheck the outbound list after changing plan/tier/region. For a fixed egress IP use VNet integration and NAT Gateway (additional cost). Keep the initial operator rule only as long as needed. A single instance is intentional: in-memory Blazor circuits and rate limits are not distributed. Keep session affinity enabled; scaling requires a separate design, not just raising instance count.

## 5. Configure the app

```bash
DB_SECRET_URI="$(az keyvault secret show --vault-name "$VAULT" --name db-connection --query id -o tsv)"
SMTP_SECRET_URI="$(az keyvault secret show --vault-name "$VAULT" --name smtp-password --query id -o tsv)"
# Strip the secret version so future rotation can resolve the latest version.
DB_SECRET_URI="${DB_SECRET_URI%/*}"
SMTP_SECRET_URI="${SMTP_SECRET_URI%/*}"
KEYS_URI="${BLOB_SERVICE%/}/${KEYS}/keyring.xml"

az webapp config appsettings set -g "$RG" -n "$APP" --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  WEBSITES_PORT=8080 \
  "ConnectionStrings__Default=@Microsoft.KeyVault(SecretUri=$DB_SECRET_URI)" \
  "App__PublicBaseUrl=$PUBLIC_URL" App__BehindProxy=true \
  "AllowedHosts=$PUBLIC_HOST" \
  "DataProtection__BlobUri=$KEYS_URI" \
  Storage__Provider=AzureBlob \
  "Storage__AzureBlob__ServiceUri=$BLOB_SERVICE" \
  "Storage__AzureBlob__ContainerName=$ATTACHMENTS" \
  Storage__EnsureContainerOnStartup=false Database__MigrateOnStartup=false \
  "Email__Smtp__Host=$SMTP_HOST" Email__Smtp__Port=587 Email__Smtp__EnableSsl=true \
  "Email__Smtp__User=$SMTP_USER" "Email__Smtp__From=$SMTP_FROM" \
  "Email__Smtp__Password=@Microsoft.KeyVault(SecretUri=$SMTP_SECRET_URI)" \
  Ai__Provider=Fake --output none
```

Do **not** set `Storage__AzureBlob__ConnectionString`, `AZURE_CLIENT_SECRET` or storage account keys in Azure. `DefaultAzureCredential` uses the app's system-assigned managed identity; on your workstation it uses `az login`. `DataProtection__BlobUri` overrides the Docker image's `/keys` default, so no `/home` or `/keys` volume is required for this deployment. The container need not be created at runtime, but the keyring **blob** is created on first use.

`App__BehindProxy=true` trusts forwarded headers; this assumes the App Service front end is the only route to the application container. Do not expose its internal HTTP port or reuse this setting behind an untrusted proxy. App Service redirects to HTTPS and ASP.NET Core uses forwarded HTTPS for cookies and HSTS.

`Fake` AI deliberately produces test drafts. Before enabling `OpenAiCompatible`, approve the provider/privacy decision (D-19), set the keys from `DEPLOYMENT.md`, and verify the provider supports this app's Chat Completions structured-output contract. Azure OpenAI is **not** automatically configured by deploying to Azure.

## 6. Verify, build and apply migrations

Before the first deployment, use the exact reviewed checkout for both image and administrative commands. Commit/tag the release through the normal review workflow before a real deployment; do not label uncommitted changes with a commit SHA. This runbook does not commit or push for you.

```bash
git status --short  # must be clean; TAG must identify the reviewed commit
dotnet build MentorPath.slnx
dotnet test tests/MentorPath.UnitTests
dotnet test tests/MentorPath.IntegrationTests

# Remote Linux/amd64 build avoids Apple Silicon image architecture mismatches.
az acr build --registry "$ACR" --image "mentorpath:$TAG" --platform linux/amd64 .

# Local administrative binary, built from the SAME checkout.
ADMIN_DIR="$(mktemp -d)"
dotnet publish src/MentorPath.Web -c Release -o "$ADMIN_DIR" /p:UseAppHost=false
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__Default="$DB_CONNECTION"
export App__PublicBaseUrl="$PUBLIC_URL"
export Storage__Provider=AzureBlob
export Storage__AzureBlob__ServiceUri="$BLOB_SERVICE"
export Storage__AzureBlob__ContainerName="$ATTACHMENTS"
export DataProtection__BlobUri="$KEYS_URI"
export Email__Smtp__Host="$SMTP_HOST" Email__Smtp__Port=587 Email__Smtp__EnableSsl=true
export Email__Smtp__User="$SMTP_USER" Email__Smtp__From="$SMTP_FROM"
export Email__Smtp__Password="$SMTP_PASSWORD"
unset Storage__AzureBlob__ConnectionString

# For an existing installation: take and verify backups FIRST (step 9).
dotnet "$ADMIN_DIR/MentorPath.Web.dll" migrate
```

The operator's `az login` credentials and Blob role allow administrative commands to share the app's key ring. The workstation IP must be allowed by the database firewall. In a later terminal session, reload names and retrieve `DB_CONNECTION` / `SMTP_PASSWORD` from Key Vault into variables (use `az keyvault secret show --query value -o tsv` in command substitution, never print them). Recreate the exports and publish the chosen release. Do not run concurrent migrations or use `Database__MigrateOnStartup=true` in production.

## 7. Start and create the first mentor

```bash
az webapp config container set -g "$RG" -n "$APP" \
  --container-image-name "$LOGIN_SERVER/mentorpath:$TAG" \
  --container-registry-url "https://$LOGIN_SERVER" --output none
az webapp log config -g "$RG" -n "$APP" --docker-container-logging filesystem --output none
az webapp start -g "$RG" -n "$APP"
# Allow a few minutes for initial image pull, RBAC propagation and startup.
curl --fail --show-error "$PUBLIC_URL/health/live"
curl --fail --show-error "$PUBLIC_URL/health/ready"

# Run locally, NOT in an Azure build/job log: stdout includes a one-time password link.
dotnet "$ADMIN_DIR/MentorPath.Web.dll" create-mentor \
  --email "<mentor-email>" --name "<display-name>" --workspace "<workspace-name>"
```

Open the printed link in a browser, set a password, and share the link only through a secure channel. The local CLI and deployed app must have identical `DataProtection__BlobUri`, application name (`MentorPath`, fixed in code), and public URL. Never run `seed-demo --force` in production.

Before inviting real users, verify:

- Log in, create a mentee invitation, accept it in another browser, and complete a roadmap exercise.
- Upload and download a small allowed file. Verify unauthenticated/foreign users cannot download it; Blob anonymous access must fail. `/health/ready` checks **PostgreSQL only**, not Blob, SMTP, Key Vault or AI.
- Use “Nie pamiętasz hasła?” and receive the reset email. Check spam/domain verification and sender settings.
- For ACS, check the SMTP username is **Ready to use**, the domain is connected and the configured From address matches its allowed sender. Verify delivery, not just the application's generic reset confirmation; that confirmation is not proof an email was sent.
- Confirm Blazor uses a `101 Switching Protocols` WebSocket connection in browser developer tools.
- Restart with `az webapp restart -g "$RG" -n "$APP"` and verify login cookies and a previously generated reset link still work. If not, fix key ring sharing before launch.
- In App Service Configuration, confirm Key Vault references have status **Resolved**. Do not paste resolved values into logs.
- Confirm a database restore and Blob/key-ring recovery on an isolated test environment (step 9).

For a custom domain, configure DNS and an App Service managed certificate, bind HTTPS, then update `App__PublicBaseUrl` and `AllowedHosts` together. Existing invitation/reset links still contain the previous host, so keep it available during transition.

## 8. Updates and rollback

Use an immutable new image tag for each reviewed release; do not overwrite an old tag.

1. Record the current image with `az webapp config container show -g "$RG" -n "$APP"` (treat configuration output as sensitive). Back up the database, attachments and key ring.
2. Schedule a maintenance window, stop the web app, build the new image, publish the same revision locally, and run `migrate` once as in step 6.
3. Set the new image as in step 7, start the app and repeat health/upload/login checks. A single-instance B1 deployment has downtime; clients must reconnect.
4. For an application-only failure with a backward-compatible schema, set the previous image tag and restart. **Rolling back an image does not undo a database migration.** For incompatible migrations, restore to a new PostgreSQL server, validate, update the Key Vault connection secret, and restart against the restored database with the matching application version. Restore attachments consistently if necessary.

Versionless Key Vault references are cached by App Service (up to 24 hours); a configuration change/restart refreshes resolution. Rotate the PostgreSQL role password and Key Vault secret together during a maintenance window. Never delete the identity key blob during an update or rotation.

For ACS SMTP rotation, create a new client secret on the **same Entra application**, update `smtp-password` in Key Vault, refresh/restart App Service and verify receipt of a password-reset email. Only then remove the old Entra secret. Alert ahead of client-secret expiry; an expired secret stops reset emails. The app's managed identity does not rotate that secret automatically. Plan and complete migration away from ACS Email **before September 30, 2028**; change SMTP settings/secret and retest both mentor and mentee resets before retiring ACS resources.

## 9. Backups, retention and administrative operations

PostgreSQL has 14-day managed PITR. Before every migration, also take a protected logical backup. In a later terminal session, reload `PGAPP_PASSWORD` from the password manager without printing it (for example `read -r -s -p "Database app password: " PGAPP_PASSWORD; printf '\n'`):

```bash
umask 077
BACKUP_DIR="<absolute-path-on-encrypted-backup-storage>"
PGPASSWORD="$PGAPP_PASSWORD" pg_dump \
  "host=$PGHOST dbname=mentorpath user=mentorpath_app sslmode=verify-full" \
  --format=custom --no-owner --file "$BACKUP_DIR/mentorpath-$(date -u +%Y%m%dT%H%M%SZ).dump"
```

Keep 14 daily and 8 weekly logical backups, with restricted access and tested rotation. `pg_restore --list` only checks readability; monthly restore to an **isolated** PostgreSQL 17 database with `pg_restore --no-owner --no-acl --dbname "<test-connection>" "<dump>"`, run migrations, then check the release scenario. For managed PITR use Azure Portal → PostgreSQL server → **Restore**, select the time and a new server name, then configure its firewall, TLS and connection secret. Never restore directly over production.

Blob/container soft delete is 30 days, with versioning disabled. This protects against deletion, **not every overwrite or account loss**, and LRS is not a separate backup. Schedule an independent encrypted copy of both private containers to an access-restricted backup account or offline backup. Preserve object names, content types and the keyring blob. Choose and document the backup retention before real users; do not store credentials in backup scripts. Azure Portal → Storage browser can restore soft-deleted blobs/containers; for independent copies restore into an isolated account first. Verify attachment bytes against database `ObjectKey`s, and test that restored Data Protection keys can read a pre-backup test token. Do not replace a live keyring with an older one without a recovery plan.

Do not expire current attachments by age. If enabling Blob versioning later, add a lifecycle rule to remove **previous versions** after 30 days, scoped to the attachment container, never `identity-keys`. Follow [DATA_RETENTION.md](DATA_RETENTION.md); after restoring older data, reapply recorded anonymization requests to the database **and** blobs.

Use the same administrative environment from step 6 for privacy requests:

```bash
dotnet "$ADMIN_DIR/MentorPath.Web.dll" export-user --user-id "<guid>" --out "<secure-path/request-id.json>"
dotnet "$ADMIN_DIR/MentorPath.Web.dll" anonymize-user --user-id "<guid>"            # preview
dotnet "$ADMIN_DIR/MentorPath.Web.dll" anonymize-user --user-id "<guid>" --confirm  # irreversible
```

Exports contain personal data; transfer securely and delete the operator copy after delivery. Before closing the terminal, remove sensitive variables:

```bash
unset PGADMIN_PASSWORD PGAPP_PASSWORD DB_CONNECTION SMTP_PASSWORD
unset ConnectionStrings__Default Email__Smtp__Password
```

Remove the workstation PostgreSQL firewall rule in Azure Portal when not administering; reopen **only your current IP** for later CLI/backup work. Do not remove the App Service outbound rules. Remove the exact temporary publish directory `$ADMIN_DIR` after use; never delete a repository or a shared directory as cleanup.

## 10. Monitoring and troubleshooting

Configure Azure Monitor alerts for failed `/health/ready` probes, HTTP 5xx, response time, memory/CPU, PostgreSQL storage/connections, and budget. Set log retention to at most 30 days and review access. Application logs are JSON; do not enable HTTP body logging, Azure SDK request-body logging or EF sensitive-data logging. Review platform/request logging so invitation/reset URL tokens are not retained in access logs.

```bash
az webapp log tail -g "$RG" -n "$APP"
```

| Symptom | Check |
|---|---|
| Container pull fails / unauthorized | `AcrPull` scope, RBAC-mode registry, `acrUseManagedIdentityCreds=true`, exact image tag, role propagation |
| Container not responding / 502 | Classic container model, `WEBSITES_PORT=8080`, Linux/amd64 image, startup logs, completed migrations |
| Storage 403 | Correct app identity, container-scoped Blob Data Contributor, role propagation, no stale connection string/environment credential overriding identity |
| Storage 404 / `ContainerNotFound` | Provision both private containers; only a missing individual blob is treated as a missing file |
| Database timeout | Current App Service possible outbound IPs and workstation firewall rule, DNS and server status |
| TLS error | Current CA trust store and Azure PostgreSQL certificate guidance; never disable certificate verification |
| Unresolved Key Vault reference | App identity's Secrets User role, correct secret URI, role propagation; restart after fixing |
| Invalid reset link / logout on restart | Shared `DataProtection__BlobUri`, Blob access, exact public URL, clock, no key deletion; `/keys` is not the Azure key ring |
| Blazor disconnects | WebSockets, Always On, one instance, affinity; restarts intentionally end live circuits |
| Mail missing | `smtp.azurecomm.net:587` with STARTTLS, linked verified domain, exact allowed From address, SMTP username **Ready to use**, Entra client-secret value/expiry, resource-scoped SMTP role, quotas and delivery status |

## Existing S3 data

This release removes the S3 adapter and ignores `Storage__S3__*`; it does **not** copy objects. New empty installations can skip this section. Old SeaweedFS Docker volumes remain untouched; switching local emulators makes existing file records unavailable until copied.

For an existing installation: schedule downtime and stop all writes, back up DB + S3 objects + existing Data Protection keys, provision Azure containers, then use a trusted transfer tool (for example AzCopy's S3-to-Blob workflow or a controlled export/upload) to copy the complete keyspace to the attachment container. Preserve every key **exactly**, including `ws/.../sub/...`, bytes and Content-Type; do not add an extra bucket-name prefix. Compare counts and cryptographic hashes against the source and every `SubmissionAttachments.ObjectKey`. Test download and anonymization on a staging restore before switching production.

Existing Identity keys also require migration: the Blob key repository stores a single XML repository document, not a directory of per-key XML files. Do not simply upload a single old key file as `keyring.xml`. Plan and test a supported key-ring transfer separately, or deliberately invalidate old sessions/reset tokens and issue new reset links after cutover. Keep source backups according to the retention policy; retire S3 credentials only after verification. Never delete source data as part of an unverified cutover.

## Teardown

Deleting a resource group deletes the running app, database and storage. Export and verify backups first, and obtain the data owner's approval. For a disposable environment only, inspect `az resource list -g "$RG" -o table`, then deliberately run `az group delete --name "$RG"` and confirm the prompt. Purge-protected Key Vaults cannot be immediately purged, and names may remain unavailable during retention. Stopping only the web app does **not** stop all charges.

Entra application registrations/service principals are tenant objects, not resources in `$RG`; deleting the resource group does not remove the dedicated SMTP registration or its secrets. After confirming it is no longer used, have an authorized tenant administrator remove that dedicated registration/credentials separately.

## Official references

- [App Service custom containers, managed image pull and port settings](https://learn.microsoft.com/azure/app-service/configure-custom-container)
- [Key Vault references in App Service](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references)
- [Azure Blob .NET authentication and upload](https://learn.microsoft.com/azure/storage/blobs/storage-blob-upload)
- [ASP.NET Core Data Protection configuration](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
- [PostgreSQL firewall rules](https://learn.microsoft.com/azure/postgresql/security/security-firewall-rules)
- [Azure Blob data protection](https://learn.microsoft.com/azure/storage/blobs/data-protection-overview)
- [ACS Email retirement and onboarding guidance](https://learn.microsoft.com/azure/communication-services/acs-retirement-and-breaking-changes-guide#acs-email)
- [Create Email Communication Services](https://learn.microsoft.com/azure/communication-services/quickstarts/email/create-email-communication-resource)
- [Provision an Azure-managed email domain](https://learn.microsoft.com/azure/communication-services/quickstarts/email/add-azure-managed-domains)
- [Connect the email domain to Communication Services](https://learn.microsoft.com/azure/communication-services/quickstarts/email/connect-email-communication-resource)
- [ACS SMTP authentication, permissions and settings](https://learn.microsoft.com/azure/communication-services/quickstarts/email/send-email-smtp/smtp-authentication)

The repository tests exercise the adapter against Azurite, not a paid Azure subscription. Resource availability, subscription policy, SMTP delivery, networking and a real Azure smoke/restore test must be verified by the operator before accepting production traffic.
