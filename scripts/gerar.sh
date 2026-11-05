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

# versão Firebase (teste): AMB='firebase' + firebase/shim.html antes do script principal
mkdir -p dist/firebase
python3 - "$SRC" dist/firebase/index.html <<'PY'
import sys
s=open(sys.argv[1],encoding='utf-8').read()
s=s.replace("const AMB='oficial'","const AMB='firebase'",1).replace("<title>Sistema FJA</title>","<title>Sistema FJA · Firebase</title>",1)
shim=open('firebase/shim.html',encoding='utf-8').read()
i=s.index('<script>\nconst AMB=')
open(sys.argv[2],'w',encoding='utf-8').write(s[:i]+shim+'\n'+s[i:])
PY

python3 scripts/embutir-revit.py dist/oficial.html dist/dev.html dist/firebase/index.html
echo "ok: dist/oficial.html ($(wc -c < dist/oficial.html) bytes)  dist/dev.html ($(wc -c < dist/dev.html) bytes)"
