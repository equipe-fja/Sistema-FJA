#!/usr/bin/env bash
# Gera as duas páginas publicáveis a partir da fonte única src/sistema-fja.html
#   dist/oficial.html  -> artifact "Sistema FJA"                 (AMB='oficial')
#   dist/dev.html      -> artifact "Sistema FJA – Desenvolvimento" (AMB='dev')
set -euo pipefail
cd "$(dirname "$0")/.."
SRC=src/sistema-fja.html
mkdir -p dist

grep -q "const AMB='oficial'" "$SRC" || { echo "ERRO: a fonte precisa ter const AMB='oficial'"; exit 1; }
grep -q "<title>Sistema FJA</title>" "$SRC" || { echo "ERRO: a fonte precisa ter <title>Sistema FJA</title>"; exit 1; }

cp "$SRC" dist/oficial.html
sed "s/const AMB='oficial'/const AMB='dev'/; s#<title>Sistema FJA</title>#<title>Sistema FJA – Desenvolvimento</title>#" "$SRC" > dist/dev.html

python3 scripts/embutir-revit.py dist/oficial.html dist/dev.html
echo "ok: dist/oficial.html ($(wc -c < dist/oficial.html) bytes)  dist/dev.html ($(wc -c < dist/dev.html) bytes)"
