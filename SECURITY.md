# Security policy

Please report vulnerabilities privately through GitHub's
**Security → Report a vulnerability** on this repository. Do not open a public issue.

You can expect an acknowledgement within a few days. Please give reasonable time
for a fix before any disclosure.

## How this repo is protected

- No secrets in the repository. GitHub Actions reaches Azure through OpenID Connect, with one
  federated credential bound to the `production` environment. The API reaches Blob Storage and
  Document Intelligence with its managed identity and holds no app secrets.
- The Azure ids and the budget alert email are `production` environment secrets, readable only by
  jobs on `main` that use that environment, and masked in workflow logs. Every such job waits for
  the approval gate.
- The only workflow, `ci-cd.yml`, runs when the owner starts it (`workflow_dispatch`). Pushes and
  pull requests, including from forks, never trigger it. Deploy jobs run only from `main`, only in
  the `production` environment, and only after the owner approves the run once on the
  `production-approval` environment.
- Actions are pinned to full commit SHAs, only GitHub-owned and listed actions may run, and the
  default token is read-only.
- `main` requires a pull request and blocks force pushes and deletion.
- Secret scanning with push protection, Dependabot alerts and Dependabot security updates are
  enabled.

## How the app protects bills

See "Privacy" in [`README.md`](README.md): 128-bit bill ids with no listing route, identical 404s
for missing, deleted and expired bills, the organizer key kept only as a SHA-256 hash and compared
in constant time, receipt photos held in memory for one request, and logs that carry only a salted
hash of the bill id.
