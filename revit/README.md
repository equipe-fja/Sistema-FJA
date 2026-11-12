# Ferramentas FJA (pyRevit)

Extensão pyRevit da FJA: aba **Ferramentas FJA** no Revit (2024 e 2027).

Esta pasta é a **fonte** da extensão. O projeto das ferramentas ligadas ao sistema está em `docs/revit-tipos-sincronizados.md`.

## Conteúdo atual (extensão 2.15.3)
- Painel **Pontos** (2.14.0; antes "Legendas"): ferramentas Legendas e Codificação.
- Nomes e Parâmetros › **Renomear Tipos** — nome do tipo = "Type Mark - Descrição".
- Tipos › **Nova composição** — cria/atualiza tipos de parede, piso, forro e cobertura a partir das composições do Sistema FJA (lógica em `lib/fja_sistema.py`).
- Pontos › **Legendas** (C#; ferramenta única desde 2.7.0, ELE e LUM desde 2.10.0) — legendas de LOG, AUT, SON, ELE e LUM numa janela com prévia: itens e textos, ordem arrastando, escala por símbolo (DWG), símbolos acrescentados, linhas, margens, largura, Type Mark e cor (do modelo, do Sistema FJA, escolhida no seletor FJA ou preto). Os ajustes ficam gravados em cada vista. Os botões antigos em Python (Legenda LOG/AUT/SON/ELE/LUM) foram retirados em 2.13.0.
- Símbolos e Detalhes › **Família do desenho** (C#, desde 2.15.0) — cria uma família de item de detalhe ou de anotação genérica com o desenho 2D selecionado (linhas, regiões, textos, componentes), com prévia, estilos e cores mantidos ou tudo preto, e substitui os originais ou posiciona (ver `docs/familia-do-desenho.md`). Painel novo: exige fechar e abrir o Revit.
- Pontos › **Codificação** (C#, desde 2.8.0) — códigos SIGLA.NN no Type Mark, nomes de família e tipo (ver `docs/codificacao-pontos.md`).
- Ícones do painel Legendas em preto (2.5.4), como os demais, com versão para tema escuro.
- Suíte › **Sobre** (desde 2.5.0) — versão da extensão e da biblioteca C# (`FJA.Revit.dll`); primeiro botão em C#.
- Suíte › **Atualizar** — baixa a versão mais recente publicada pelo sistema (banco: `revitPub/extensao-versao` e `revitPub/extensao`, gravados por `extDbPublicar` na página Firebase), grava na pasta da extensão em uso (espelhando, menos `.pyc`) e recarrega o pyRevit (lógica em `lib/fja_suite.py`).
- Links › **Copiar do link**, **Verificar alterações** e **Reconectar** — em C# desde a 2.16.0 (`revit/dotnet/FJA.Revit/Links/`; módulos em `lib/fja_links_modulos.json`; `lib/fja_links.py` é a versão Python antiga, sem uso). Módulos Equipamentos (peças) e Encaminhamentos (redes). Ver `docs/revit-links.md`.

Origem: cópia de `%APPDATA%\pyRevit\Extensions\Ferramentas-FJA.extension` de um computador do escritório (03/11/2026).

## Biblioteca C# (desde 2.5.0)
Ferramentas novas e avançadas são feitas em C# (`revit/dotnet/FJA.Revit`), compiladas pelo GitHub Actions para `bin/R2024` e `bin/R2027` e chamadas pelos botões do pyRevit via `lib/fja_dotnet.py`, que carrega a DLL pela memória (sem travar o arquivo; dá para atualizar com o Revit aberto). Detalhes, critérios de migração e como criar um comando: `docs/revit-csharp.md`.

## Distribuição
- **Pelo sistema (desde 06/11/2026):** Biblioteca › Ferramentas › Extensão do Revit › "Baixar o instalador". O .zip traz a extensão, `instalar.cmd` e um LEIA-ME. O instalador (`revit/instalar.ps1`, embutido por `scripts/embutir-revit.py`) copia a extensão para a pasta compartilhada do G: com `robocopy /MIR` (não substitui versão mais nova), cadastra a pasta em `pyRevit_config.ini` ([core] userextensions; cópia anterior em `.antes-fja`) e move a cópia local antiga de `%APPDATA%\pyRevit\Extensions` para `%APPDATA%\pyRevit\_backup-extensoes`. Sem o G:, instala em `%APPDATA%\pyRevit\Extensions`. Testado no Windows com G: e APPDATA simulados (variáveis `FJA_TESTE_REVIT` e `FJA_TESTE_APPDATA`).
- Cópia de uso no G:: `G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT\pyRevit\Ferramentas-FJA.extension` (em uso desde 03/11/2026 no primeiro computador).
- Cada computador aponta o pyRevit para `G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT\pyRevit` (pyRevit › Settings › Custom Extension Directories, ou `pyrevit extensions paths add "<pasta>"`) e remove a cópia local de `%APPDATA%\pyRevit\Extensions` para não carregar a aba duas vezes.
- Para atualizar: alterar aqui, commit, copiar a pasta para o G: e recarregar o pyRevit (Reload).

## Legendas LOG, AUT e SON (2.4.0, 09/11/2026)
- Mesma lógica nos três botões, em `lib/fja_legenda_disciplina.py`; o script de cada botão só traz a configuração (nome da vista, título, prefixos das famílias e das vistas, legendas de base, marca e cor padrão).
- LOG: famílias `(LOG)`, vistas `LOG`, base `LOG - LEGENDA GRAFICA`. AUT: famílias `(AUT)`, vistas `AUT`, base `AUT - LEGENDA…` ou, se não houver, `LOG`/`ELE - LEGENDA GRAFICA`. SON: famílias `(SON)` ou `(SNZ)`, vistas `SON`/`SNZ`, base `SON`/`SNZ - LEGENDA…` (no modelo 129, `SNZ - LEGENDA SONORIZAÇÃO`).
- Textos alinhados pela esquerda, todos depois do símbolo mais largo. Símbolos copiados da planta maiores que 12 mm de papel são reduzidos (ex.: rack).
- Cores no 129: LOG 121-86-169, AUT 255-110-45, SON 140-195-25 (filtros `SISTEMA - …`); os ícones usam essas cores.
- Itens: um por tipo de família cujo nome começa com `(LOG)` e que aparece em alguma vista colocada em prancha (planta, corte, etc.; legendas e tabelas não contam). Texto = nome da família sem o `(LOG)`, em maiúsculas; se a família tiver mais de um tipo na legenda, acrescenta ` - <tipo>`. Ordem: categoria e texto.
- Cor de cada item = a cor das linhas do elemento nas vistas das pranchas (a mais frequente, preferindo vistas cujo nome começa com `LOG`): sobreposição do elemento › filtros da vista ou do modelo de vista (na ordem de prioridade, só os ativos e visíveis) › sobreposição da categoria › estilo de objeto. No modelo 129 sai o roxo 121-86-169 do filtro `SISTEMA - LÓGICA`; o ícone do botão usa esse roxo.
- Símbolo (desde 2.4.1): cada item usa uma **família de anotação genérica `(DISC) (LEG) NOME`**, ex.: `(SON) (LEG) ANTENA DE MICROFONE`, `(LOG) (LEG) CAMERA BULLET`, `(AUT) (LEG) SENSOR ANALOGICO` (prefixo da disciplina do botão; SNZ vira SON; sufixo `.0001` de cópia é ignorado). Se o projeto já tem a família com esse nome, ela é usada como está (pode ser ajustada à mão ou trazida da biblioteca); senão o botão cria a partir do modelo "Anotação genérica métrica" com as anotações aninhadas da família (mesma posição e parâmetros) ou, sem elas, as linhas do elemento na planta, em tamanho de papel (máx. 12 mm, mín. 4,5 mm), e carrega no projeto (cópia do .rfa em `%TEMP%\FJA_LEG`). Se não der para criar, cai no método antigo (anotações/linhas desenhadas na vista ou componente de legenda). Tudo pintado na cor do item.
- Ícones AUT e SON (2.5.1): mesma placa de cantos arredondados de LOG e ELE (mesmo tamanho e espessura de traço), com o símbolo dentro (ondas de sensor; alto-falante).
- Base: duplica `LOG - LEGENDA GRAFICA` (estilos de título, texto e sublinhado). Alguns símbolos são DWG importados na família; se a legenda tiver modelo de vista que esconde "Importados em famílias", o modelo é retirado da legenda e a categoria fica visível (avisado no relatório).
- Marca: Extensible Storage `FJA_LegendaLOG`; nova execução apaga só o que o botão criou e lista o que entrou e saiu.
- Testado no Revit 2027 no modelo 129-AUT_LOG_SON (transações desfeitas): LOG 6 itens, AUT 1 (sensor analógico), SON 5 (antena de microfone, microfone, sonorefletor, rack, controle de volume). 2.4.1: famílias (LEG) criadas para LOG (5), AUT (2) e SON (5), sem erros.

## Copiar do link (desde 06/11/2026)
- Módulos: **Equipamentos** (liberado: mobiliário, marcenaria, equipamentos, aparelhos sanitários, luminárias, tomadas e demais dispositivos, sprinklers, terminais de ar), **Estrutura**, **Arquitetura** e **Encaminhamentos** (ainda "em breve"). As categorias de cada módulo ficam em `lib/fja_links_modulos.json`; cada categoria pertence a um módulo só.
- Janela: escolha do link e do módulo; lista agrupada por categoria · família · tipo · nível, com filtros e situação (novos / já copiados / precisam de hospedeiro); workset de destino; "Escolher no modelo…" para selecionar elementos do link na vista (ou selecionar com Tab antes de abrir). Sem linhas selecionadas, copia os novos de todas as linhas visíveis.
- Cópia com `ElementTransformUtils.CopyElements` usando a posição da instância do link: mesma posição e rotação, inclusive espelhados; tipos com o mesmo nome usam o tipo do modelo. Famílias **face based** vêm **sem hospedeiro**, no mesmo lugar. Famílias hospedadas em parede/forro/piso (não face based) não copiam e aparecem como "precisam de hospedeiro". Componentes aninhados compartilhados vêm com o pai; opções de projeto secundárias ficam de fora.
- O registro guarda também a posição do link (`lt`), a posição da cópia (`c`) e os parâmetros do módulo (`p`, lista `parametros` em `fja_links_modulos.json`; padrão Marca e Comentários) — base do Verificar alterações. Cópias feitas antes da 2.3.0 são comparadas sem esses campos (só posição, tipo e nível).
- Cada cópia recebe o registro `FJA_CopiaLink` (Extensible Storage): módulo, link, UniqueId da instância do link, UniqueId do elemento de origem, foto do estado da origem (família, tipo, posição e orientação no link, espelhado, nível), data e usuário. Serve para não copiar duas vezes e é a base do monitoramento (próxima etapa: "Verificar alterações").
- Testado no Revit 2027 com modelos temporários (link deslocado e girado, nível com cota diferente, espelhado, face based em laje e em parede, tomada hospedada em parede).

## Dados do sistema
O sistema publica em `G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT` (desenvolvimento: `REVIT\DEV`):
- `biblioteca.json` — siglas, itens centrais (com `camadas` e `rvCat` nos itens de composição) e lista de projetos.
- `projeto-<código>.json` — especificação de cada projeto (keynote, item central, descrição).

Cada publicação cria o arquivo novo e manda o anterior para a lixeira do Drive. Se houver mais de um arquivo com o mesmo nome (ex.: `biblioteca (1).json`), usar o mais recente.

## Convenções dos scripts
- IronPython (pyRevit), `# -*- coding: utf-8 -*-`, textos em português.
- `bundle.yaml` com `title`/`tooltip` em `pt_br`.
- Elementos criados por uma ferramenta são marcados com Extensible Storage (ex.: `FJA_LegendaELE`, `FJA_LegendaLOG`); os tipos ligados ao sistema usarão o esquema `FJA_SistemaTipo`.

## Observações de implementação
- `lib/fja_sistema.py` não depende do pyRevit (recebe o documento): dá para testar pelo conector do Revit com `sys.path` apontando para a pasta `lib`.
- Para trocar camadas, parte-se da estrutura do tipo-base (`GetCompoundStructure` + `SetLayers`): criar a estrutura do zero falha em pisos ("wrong EndCap condition").
- Ao enviar arquivos para o G: pela ponte do Claude, usar um nome de origem novo a cada envio (reenviar pelo mesmo caminho não substituiu o conteúdo).

## Como apontar um computador para o G:
1. Fechar o Revit.
2. Em `%APPDATA%\pyRevit\pyRevit_config.ini`, seção `[core]`: `userextensions = ["G:\\Drives compartilhados\\BI-BIBLIOTECA\\SISTEMA FJA\\REVIT\\pyRevit"]` (ou pyRevit › Settings › Custom Extension Directories).
3. Tirar `Ferramentas-FJA.extension` de `%APPDATA%\pyRevit\Extensions` (mover para outra pasta), para a aba não aparecer duas vezes.
4. Abrir o Revit (ou pyRevit › Reload).

## Ordem dos painéis (desde 2.16.1)
- `Ferramentas FJA.tab/bundle.yaml` (`layout`) define a ordem: painéis em ordem alfabética e **Suíte sempre no fim**.
- No pyRevit, painel que não está no `layout` **não aparece**. O `scripts/embutir-revit.py` recalcula a lista a cada publicação e o pacote distribuído (instalador e Suíte › Atualizar) sempre leva todos os painéis, com Suíte no fim; se o arquivo do repositório estiver desatualizado, a compilação avisa — atualizar o arquivo ao criar um painel novo.
