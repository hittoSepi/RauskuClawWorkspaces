# Task 055: HOLVI auto-enable and stack-specific env split

## Status
Completed

## Summary
Cloud-init provisioning and wizard secret flow were updated so HOLVI (`infra/holvi`) is treated as a separate stack from backend runtime env.

## What changed
- Split cloud-init env preflight into backend and HOLVI specific paths:
  - backend path keeps `API_KEY` / `API_TOKEN` readiness checks,
  - HOLVI path no longer runs backend API token enforcement.
- Added stack-specific provisioning secret allowlists:
  - backend allowlist: `API_KEY`, `API_TOKEN`, `HOLVI_BASE_URL`, `HOLVI_PROXY_TOKEN`, `OPENAI_ENABLED`, `OPENAI_SECRET_ALIAS`
  - HOLVI allowlist: `PROXY_SHARED_TOKEN`, `INFISICAL_BASE_URL`, `INFISICAL_PROJECT_ID`, `INFISICAL_SERVICE_TOKEN`, `INFISICAL_ENCRYPTION_KEY`, `INFISICAL_AUTH_SECRET`, `INFISICAL_ENV`, `HOLVI_INFISICAL_MODE`, `HOLVI_BIND` and hardening keys.
- Added HOLVI mode controls to provisioning request:
  - `EnableHolvi`
  - `HolviMode` (`Disabled` / `Enabled`)
- Wizard now requests extended secret key set and derives HOLVI auto-enable from secret-manager credential readiness (`CredentialsConfigured`).
- Root runtime `.env` gets full HOLVI mode wiring when HOLVI is enabled:
  - `OPENAI_ENABLED=1`
  - `OPENAI_SECRET_ALIAS=sec://openai_api_key` (if placeholder/missing)
  - `HOLVI_BASE_URL=http://holvi-proxy:8099`
  - `HOLVI_PROXY_TOKEN` synchronized from HOLVI env shared token.
- HOLVI env placeholder generation added for missing values:
  - generated random: `PROXY_SHARED_TOKEN`, `INFISICAL_ENCRYPTION_KEY`, `INFISICAL_AUTH_SECRET`
  - generated placeholders: `INFISICAL_PROJECT_ID`, `INFISICAL_SERVICE_TOKEN`
  - explicit warning logs emitted for operator follow-up.
- HOLVI compose now defaults to shared-Infisical mode:
  - `INFISICAL_BASE_URL` defaults to `http://host.docker.internal:8088`,
  - `HOLVI_INFISICAL_MODE=shared` by default,
  - local Infisical (`postgres` + `redis` + `infisical`) runs only with compose profile `local-infisical` (`HOLVI_INFISICAL_MODE=local`).
- Added wizard stage `holvi` and serial-stage promotion hooks for HOLVI startup signals.

## Validation
- Updated and passed targeted tests:
  - `ProvisioningScriptBuilderHardeningTests`
  - `SecretsAdapterAndWizardDecisionTests`
  - `ProvisioningSecretsServiceTests`
  - existing startup reason/stability test slice remains passing.

## Notes
- `CredentialsConfigured` only indicates that secret-manager credentials are present (Holvi or Infisical login path), not that all required HOLVI runtime values are valid.
- Infisical `projectId` and `service token` placeholders intentionally require manual replacement before production secret fetch succeeds.
- In shared mode, workspace-level isolation is expected from unique `INFISICAL_PROJECT_ID` + `INFISICAL_SERVICE_TOKEN` values per workspace.
