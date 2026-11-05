#!/usr/bin/env bash
# Grava o log da última execução na branch firebase-log (logs/<tarefa>.txt), para consulta sem abrir o GitHub.
set -uo pipefail
tarefa="$1"; status="$2"; log="$RUNNER_TEMP/log.txt"; [ -f "$log" ] || echo "(sem log)" > "$log"
d=$(mktemp -d); cd "$d"
git init -q; git config user.name "github-actions"; git config user.email "actions@github.com"
git remote add origin "https://x-access-token:${GITHUB_TOKEN:-}@github.com/${GITHUB_REPOSITORY}.git"
git fetch -q origin firebase-log 2>/dev/null && git checkout -q -b firebase-log FETCH_HEAD || git checkout -q --orphan firebase-log
mkdir -p logs
{ echo "tarefa: $tarefa"; echo "status: $status"; echo "commit: $GITHUB_SHA"; echo "quando: $(date -u +%FT%TZ)"; echo "----"; tail -c 60000 "$log"; } > "logs/$tarefa.txt"
git add logs && git commit -qm "log: $tarefa $status" && git push -q origin HEAD:firebase-log
