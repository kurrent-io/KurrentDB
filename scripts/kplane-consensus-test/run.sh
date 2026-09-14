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

# CLUSTER_SIZE drives the node configs, which services start, and the diagnostics below, so the
# three can never disagree about how big the cluster is. Only 3 and 5 are wired up.
CLUSTER_SIZE=$(sed -n 's/^CLUSTER_SIZE=//p' .env 2>/dev/null | tail -1)
CLUSTER_SIZE="${CLUSTER_SIZE:-3}"
case "$CLUSTER_SIZE" in
	3|5) ;;
	*) echo "CLUSTER_SIZE must be 3 or 5, got '$CLUSTER_SIZE'" >&2; exit 2 ;;
esac

PROFILE_ARGS=()
[ -n "$PROFILE" ] && PROFILE_ARGS=(--profile "$PROFILE")
[ "$CLUSTER_SIZE" = 5 ] && PROFILE_ARGS+=(--profile n5)

# Regenerated every run: ClusterSize and the seed lists must match what actually starts.
./gen-conf.sh "$CLUSTER_SIZE"
echo "running a $CLUSTER_SIZE-node cluster (quorum $(( CLUSTER_SIZE / 2 + 1 )))"

cleanup() { docker compose "${PROFILE_ARGS[@]}" down -v --remove-orphans >/dev/null 2>&1 || true; }

docker compose "${PROFILE_ARGS[@]}" up -d --build

# Stop injecting faults the moment the checker starts verifying. Pumba has no shell and no total
# duration, so it otherwise keeps killing nodes while the ledger is being read back - and at a high
# fault rate the cluster never becomes readable, which turns the acknowledged-write check (the most
# important one) into a permanent "inconclusive". The supervisor keeps running, so nodes still come
# back for verification.
(
	docker compose logs -f checker 2>&1 | grep -q 'duration reached' || true
	echo ">>> run finished, stopping fault injection before verification"
	docker compose "${PROFILE_ARGS[@]}" stop chaos-crash chaos-hang >/dev/null 2>&1 || true
) &

# `logs -f` follows until the checker exits, so this both streams the run and blocks on it.
docker compose logs -f checker || true

code=$(docker inspect kplane-checker --format '{{.State.ExitCode}}' 2>/dev/null || echo 1)

echo
echo "=== nodes (each must be running again at the end) ==="
for n in $(seq 1 "$CLUSTER_SIZE"); do
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
