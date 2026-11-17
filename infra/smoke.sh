#!/usr/bin/env bash
# Smoke test against a running API: create the sample bill, claim one unclaimed item, check the
# totals, then delete the bill. Leaves nothing behind, even when a check fails.
#
#   infra/smoke.sh https://<api-host>
set -euo pipefail

api=${1:?Usage: $0 https://<api-host>}
api=${api%/}

fail() { echo "Smoke test failed: $*" >&2; exit 1; }

# The API scales to zero, so the first request may wait for a container to start.
for attempt in $(seq 1 30); do
  curl -fsS "$api/healthz" >/dev/null 2>&1 && break
  [[ $attempt -eq 30 ]] && fail "/healthz did not answer"
  sleep 5
done

created=$(curl -fsS -X POST "$api/api/bills" -H 'content-type: application/json' -d '{"source":"sample"}')
id=$(jq -r .id <<<"$created")
key=$(jq -r .organizerKey <<<"$created")
[[ -n "$id" && "$id" != null && -n "$key" && "$key" != null ]] || fail "create returned no id or key"
trap 'curl -fsS -o /dev/null -X DELETE "$api/api/bills/$id" -H "X-Organizer-Key: $key" || true' EXIT

before=$(curl -fsS "$api/api/bills/$id")
unit=$(jq -r '[.bill.receipts[0].lines[].units[] | select(.claims | length == 0)][0].id' <<<"$before")
price=$(jq --arg unit "$unit" '.bill.receipts[0].lines[] | select(any(.units[]; .id == $unit)) | .unitPrice' <<<"$before")
person=$(jq -r '.bill.people[0].id' <<<"$before")
unclaimed_before=$(jq '.breakdown.receipts[0].unclaimed.subtotal' <<<"$before")
[[ "$unit" != null && -n "$price" ]] || fail "the sample bill has no unclaimed unit"

# As a friend: pick a name, then claim with the person token that comes back in a header.
token=$(curl -fsS -o /dev/null -D - -X PATCH "$api/api/bills/$id" -H 'content-type: application/json' \
  -d "{\"op\":\"pick\",\"personId\":\"$person\"}" | tr -d '\r' | awk -F': ' 'tolower($1) == "x-person-token" { print $2 }')
[[ -n "$token" ]] || fail "picking a name gave no person token"
after=$(curl -fsS -X PATCH "$api/api/bills/$id" -H 'content-type: application/json' -H "X-Person-Token: $token" \
  -d "{\"op\":\"claim\",\"unitId\":\"$unit\",\"personId\":\"$person\",\"from\":[]}")

jq -e --argjson expected "$((unclaimed_before - price))" \
  '.breakdown.receipts[0].unclaimed.subtotal == $expected' <<<"$after" >/dev/null \
  || fail "the claimed item did not leave the unclaimed bucket"
jq -e '.breakdown.receipts[0] | .computedTotal == .confirmedTotal' <<<"$after" >/dev/null \
  || fail "the sample receipt no longer matches its printed total"
jq -e '.breakdown.receipts[0] | ([.people[].total] | add) + .unclaimed.total == .computedTotal' <<<"$after" >/dev/null \
  || fail "the split does not add up to the receipt total"

curl -fsS -o /dev/null -X DELETE "$api/api/bills/$id" -H "X-Organizer-Key: $key" || fail "delete was refused"
trap - EXIT
[[ $(curl -s -o /dev/null -w '%{http_code}' "$api/api/bills/$id") == 404 ]] || fail "the deleted bill is still readable"

echo "Smoke test passed: create, claim, totals, delete."
