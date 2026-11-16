# Infrastructure

Bicep lives here. Module 3 in `docs/EXECUTION_PLAN.md` adds:

- `main.bicep`: entry point
- `modules/`: storage, docintel, containerapp, staticwebapp, roles, budget
- `main.bicepparam`: region, names, budget amount (no secrets)
- `setup-oidc.sh`: one-time GitHub to Azure sign-in setup (federated credential, no stored keys)
