# Notas técnicas

## Banco de dados
- Cada ambiente (oficial e dev) tem o próprio banco (capacidade `db` do artifact). Documentos em coleções; leitura em tempo real com `onSnapshot`; gravações conferem a versão (`if_version`).
- Coleções: `itens` (projetos, disciplinas, parceiros — campo `secao`), `coment`, `pend`, `equip`, `rdoc`, `crono`, `ata`, `croq`, `espec`, `espb`, `sugest`; documentos `config/schema`, `config/espelhos`.
- IDs de registros por projeto costumam começar pelo projeto (`prj-131__ata002`, `prj-128__A-B.1`).
- Campos do projeto ficam em `campos` (esquema em `config/schema`, `garantirCodCliente` injeta `codCliente`).
- Estado de backups/vínculos no projeto: `comEspelho`, `pdEspelho`, `equipEspelho`, `croEspelho`, `rdOrigem`/`rdEspelho` (`…Off` = parado; `…Dev` = liberado na dev), `ataPasta`, `croqPasta`.
- Imagens (capas, fotos) ficam nos assets do ambiente oficial e podem não aparecer na dev.
- Cópia de dados oficial → dev: `db_list` das duas bases para arquivos, comparar, gravar em lotes de 50 com `if_version`.

## Módulos
- Listas por projeto seguem `iniciarMod` (cm, pd, eq, rd). Para um módulo novo: coleção + mapa em `S`, `onSnapshot`, `MAPA_COLL`, mapeamento coleção→módulo no agendamento do backup, `ESP`, `SEM_DESF`, `M()`, `VISTA_LISTA`, rotas (lista e ficha via `telaSplit`), árvore lateral, painel do projeto e `PDFL_*`. `ordPadrao` define a ordenação inicial.
- Fora desse molde: cronograma, atas, croquis (estado em `S.atas`/`S.croqs`, gravação automática com debounce de 700 ms).
- Biblioteca › Ferramentas: lista `FERRAMENTAS`, tela `telaFerr`.

## Sincronização com o Drive
- Mão única: o sistema é a base; planilhas no Drive são backups de leitura, reescritos 6 s após a última alteração; pendências de quem estava sem conexão são enviadas por qualquer pessoa conectada (verificação a cada 90 s).
- Comentários, Pendências, Equipamentos e RD: a planilha vinculada é a origem da importação e depois o próprio backup.
- Cronograma, Disciplinas e Parceiros: destino do backup escolhido no Explorer.

## Google Sheets / Drive pelos conectores
- Capacidades: Google Sheets [get_values, get_spreadsheet, update_values, update_spreadsheet], Google Drive [create_file, get_file_metadata, download_file_content, search_files].
- `repeatCell`: máscara `fields` separada por vírgulas (sem parênteses). Não dá para congelar colunas com células mescladas atravessando a divisória. Formatação condicional com ";" e no idioma da planilha. Aba nova tem 26 colunas: `appendDimension` antes de passar da Z.
- Drive: pastas do 1º nível de um drive compartilhado têm como `parentId` a raiz do drive (PR-PROJETOS = `0AAq0Xc751WuPUk9PVA`; BI-BIBLIOTECA = `0AGDy8xQxEIAtUk9PVA`), diferente do `sharedDriveId`. `search_files` aceita `parentId='<raiz>'`, mas não o `sharedDriveId`.
- `mcp.callTool('Google Drive','create_file',…)` cria pastas, Google Docs a partir de HTML e binários (`base64Content`, `disableConversionToGoogleType`).

## PDF (pdf-lib)
- `drawSvgPath` usa coordenadas SVG (y para baixo) relativas ao x/y dado. Cuidado com substituições por regex: renomear `M` alterou o comando `M` de caminhos SVG.
- `montarPdfLista` atende listas e, no cronograma, um ramo com gantt. `montarPdfAta` para atas.

## Windows — Conexão com PC
- Pacote gerado pela página (zip sem compressão); vigia em `%LOCALAPPDATA%\FJA\ConexaoPC`, link `fja-abrir:`. Só aceita caminhos no G:.
- Janelas abertas por processo oculto aparecem atrás do navegador: v6 usa AttachThreadInput + SetForegroundWindow.
- A marcação "Já instalei neste computador" é por navegador e por ambiente.
