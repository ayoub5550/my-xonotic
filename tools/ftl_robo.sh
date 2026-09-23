#!/usr/bin/env bash
# Run the APK on real phones in Firebase Test Lab and pull the results.
# Usage: FIREBASE_SA_JSON=~/secrets/firebase-sa.json tools/ftl_robo.sh <apk> <label> [robo|game-loop]
# See docs/DEVICE-TESTING.md. Never commit the key or Artifacts/.
set -euo pipefail
APK="${1:?apk path}"; LABEL="${2:?label e.g. dev16}"; MODE="${3:-robo}"
PROJECT="${FTL_PROJECT:-ayoub-261d7}"
DEVICES="${FTL_DEVICES:-model=a15x,version=34,locale=en,orientation=landscape model=SC-51E,version=36,locale=en,orientation=landscape}"
OUT="Artifacts/ftl/$LABEL"; mkdir -p "$OUT"
if [ -n "${FIREBASE_SA_JSON:-}" ]; then
  gcloud auth activate-service-account --key-file="$FIREBASE_SA_JSON" >/dev/null
fi
gcloud config set project "$PROJECT" >/dev/null
DEV_ARGS=(); for d in $DEVICES; do DEV_ARGS+=(--device "$d"); done
RESULTS_DIR="$LABEL-$(date -u +%Y%m%d-%H%M%S)"
set +e
gcloud firebase test android run --type "$MODE" --app "$APK" "${DEV_ARGS[@]}" \
  --timeout 6m --results-dir "$RESULTS_DIR" --format=json > "$OUT/run.json" 2> "$OUT/run.err"
RC=$?
set -e
cat "$OUT/run.err" | grep -E "Raw results|Test results|Passed|Failed|Inconclusive|ERROR" || true
BUCKET=$(grep -o 'gs://[^ ]*' "$OUT/run.err" | head -1 | sed 's#/\?$##')
if [ -n "$BUCKET" ]; then
  gsutil -m cp -r "$BUCKET" "$OUT/" >/dev/null 2>&1 || echo "gsutil copy failed"
fi
echo "FTL exit=$RC results=$OUT"
exit $RC
