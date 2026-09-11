#!/usr/bin/env bash
# Runs the consensus test and exits with the checker's verdict: 0 if every invariant held.
#
#   ./run.sh            # crashes + freezes + resignations
#   ./run.sh crash      # crashes only
#   ./run.sh hang       # freezes only
#   ./run.sh ""         # baseline: resignations only, no Pumba
#
# Deliberately NOT `docker compose up --abort-on-container-exit --exit-code-from checker`: that
# flag stops every container as soon as any one of them exits, and Pumba exits nodes constantly by
# design. Detached, the run survives its own chaos and the verdict comes from the checker.
set -euo pipefail
cd "$(dirname "$0")"

PROFILE="${1-chaos}"
PROFILE_ARGS=()
[ -n "$PROFILE" ] && PROFILE_ARGS=(--profile "$PROFILE")

cleanup() { docker compose "${PROFILE_ARGS[@]}" down -v --remove-orphans >/dev/null 2>&1 || true; }

docker compose "${PROFILE_ARGS[@]}" up -d --build

# `logs -f` follows until the checker exits, so this both streams the run and blocks on it.
docker compose logs -f checker || true

code=$(docker inspect kplane-checker --format '{{.State.ExitCode}}' 2>/dev/null || echo 1)

echo
echo "=== nodes (each must be running again at the end) ==="
for n in 1 2 3; do
	printf '  kplane-node%s: ' "$n"
	docker inspect "kplane-node$n" --format 'state={{.State.Status}} policyRestarts={{.RestartCount}}' 2>/dev/null || echo "gone"
done

# Pumba kills through the Docker API, and Docker ignores a restart policy for API-stopped
# containers - so the supervisor, not RestartCount, is what actually brings nodes back. If chaos
# ran and this count is 0, nodes are not recovering and the run measured nothing after the first
# few kills.
echo
echo "=== supervisor restarts ==="
printf '  %s node restart(s)\n' "$(docker logs kplane-supervisor 2>&1 | grep -c 'restarting' || echo 0)"

echo
echo "=== chaos containers (must NOT have exited early) ==="
docker compose "${PROFILE_ARGS[@]}" ps -a --format '  {{.Name}}: {{.State}} {{.Status}}' 2>/dev/null | grep chaos || echo "  none (no chaos profile)"

trap cleanup EXIT
echo
echo "checker exit code: $code"
exit "$code"
