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

# versões Firebase: dist/firebase (oficial, projeto sistema-fja) e dist/homolog (projeto sistema-fja-homolog)
mkdir -p dist/firebase dist/homolog
cp firebase/sw.js dist/firebase/sw.js; cp firebase/sw.js dist/homolog/sw.js
python3 - "$SRC" <<'PY'
import sys, re
src=open(sys.argv[1],encoding='utf-8').read()
shim=open('firebase/shim.html',encoding='utf-8').read()+'\n'+open('firebase/google.html',encoding='utf-8').read().replace("/*G_CLIENT*/''",repr(open('firebase/google-client.txt').read().strip()))
def cfg(arq):
    t=open(arq,encoding='utf-8').read()
    return re.search(r'\{.*\}',t,re.S).group(0)
for saida,arq,homolog,titulo in [('dist/firebase/index.html','firebase/config.js',False,'Sistema FJA'),
                                 ('dist/homolog/index.html','firebase/config-homolog.js',True,'Sistema FJA · Homologação')]:
    s=src.replace("const AMB='oficial'","const AMB='firebase'",1).replace("<title>Sistema FJA</title>","<title>"+titulo+"</title>",1)
    sh=shim.replace('/*FB_CFG*/null',cfg(arq))
    if homolog: sh='<script>window.FJA_FB_HOMOLOG=true</script>\n'+sh
    else:
        import json; v=json.load(open('firebase/virada.json')).get('virada',False)
        sh='<script>window.FJA_FB_VIRADA='+('true' if v else 'false')+'</script>\n'+sh
    i=s.index('<script>\nconst AMB=')
    open(saida,'w',encoding='utf-8').write(s[:i]+sh+'\n'+s[i:])
PY

python3 scripts/embutir-revit.py dist/oficial.html dist/dev.html dist/firebase/index.html dist/homolog/index.html
echo "ok: dist/oficial.html ($(wc -c < dist/oficial.html) bytes)  dist/dev.html ($(wc -c < dist/dev.html) bytes)"
