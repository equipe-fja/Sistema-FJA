# Ferramentas FJA — biblioteca C# (FJA.Revit)

Desde a extensão 2.5.0 (09/11/2026) a suíte tem uma parte em C#, ao lado dos scripts do pyRevit.

## Decisão
- A suíte continua no pyRevit, que por enquanto funciona só como "casca" (aba, botões, atualização).
- **Ferramentas novas e avançadas nascem em C#.** As que já existem em Python só migram quando houver motivo concreto (ver critérios abaixo), uma a uma.
- Objetivo: não acumular código em IronPython 2.7 (Python 2, sem futuro) e não ficar travado no pyRevit. Quando quase tudo estiver em C#, troca-se a casca por um `.addin` próprio — os comandos já são `IExternalCommand` e não precisam ser reescritos.

## Como está montado
| Peça | Onde |
|---|---|
| Código C# | `revit/dotnet/FJA.Revit/` (`Entrada.cs`, `Nucleo/ComandoFJA.cs`, `Comandos/*.cs`) |
| Compilação | GitHub Actions `revit-dotnet.yml` (Windows, .NET 10 SDK). Roda quando muda `revit/dotnet/**` ou a versão em `extension.json`. Compila **R2024** (net48, Revit 2024) e **R2027** (net10.0-windows, Revit 2027) com a versão da extensão e grava `FJA.Revit.dll` + `.pdb` em `revit/Ferramentas-FJA.extension/bin/R2024` e `bin/R2027`, com commit automático na mesma branch. Na `main`, depois do commit, dispara de novo a publicação da homologação. |
| Referências do Revit | pacotes NuGet `Nice3point.Revit.Api.RevitAPI/RevitAPIUI` (`$(RevitVersion).*`), só para compilar |
| Carregador | `lib/fja_dotnet.py`: escolhe a pasta pela versão do Revit (≤2024 → R2024; ≥2027 → R2027; 2025/2026 sem suporte) e carrega a DLL **pela memória** (`Assembly.Load(bytes, pdb)`), com cache no AppDomain por caminho + tamanho + data |
| Botão de teste | Suíte › **Sobre**: versão da extensão e da DLL, Revit, .NET, forma de carregamento, comandos C# disponíveis; avisa se a DLL e a extensão estiverem em versões diferentes |

### Por que carregar pela memória
- O Windows não trava o arquivo: o Suíte › Atualizar substitui a DLL com o Revit aberto, e quem desenvolve recompila e testa de novo **sem reiniciar o Revit**.
- Cada versão nova da DLL fica na memória até o Revit fechar (o .NET não descarrega). Para quem usa, é uma carga por sessão; em dia de muitos testes, reiniciar de vez em quando.
- Cuidados: XAML de WPF compilado no projeto é localizado pelo nome do assembly e pode pegar uma versão antiga carregada antes — preferir janelas montadas em código ou XAML lido de recurso embutido (`XamlReader`). Dependências NuGet com DLL própria precisam de um resolvedor (`AssemblyResolve`) que também carregue pela memória — montar quando a primeira for necessária.

### Comando novo
1. Classe em `revit/dotnet/FJA.Revit/Comandos/`, herdando de `ComandoFJA` e implementando `Executar(UIApplication app, ref string mensagem)`.
2. Botão do pyRevit com `script.py` de duas linhas:
   ```python
   import fja_dotnet as D
   D.rodar_botao("NomeDaClasse", __revit__, u"Título")
   ```
3. Commit/push: o GitHub Actions compila e grava as DLLs.

### O que exige reiniciar o Revit
Só o que o Revit aceita na abertura: monitoramento em tempo real (`IUpdater`), painéis encaixáveis e botões novos na faixa. Fazer esse registro como um "gancho" fino (no `startup.py` do pyRevit ou, no futuro, no `.addin`) que chama a lógica da DLL recarregável.

### Teste pelo conector do Revit
O conector roda IronPython (`doc`, `uidoc`): `sys.path` para a pasta `lib` da extensão, `import fja_dotnet`, `fja_dotnet.executar("Comando", uidoc.Application, dll=r"...\FJA.Revit.dll")`. O `tempfile.gettempdir()` do conector muda a cada execução — usar pasta fixa (ex.: `%LOCALAPPDATA%\fja-teste-dotnet`).

## Distribuição (Suíte › Atualizar e instalador)
- `scripts/embutir-revit.py` separa `bin/` numa lista própria (`EXT_REVIT.bin`).
- A página Firebase (`extDbPublicar`) grava a biblioteca C# em `revitPub/extensao-bin-<i>` {versao, hash, i, n, txt}, em partes de até 900 mil caracteres (limite de 1 MB por documento), e registra `binHash`/`binPartes` em `revitPub/extensao-versao`. Os scripts continuam em `revitPub/extensao`, como antes (compatível com o Atualizar da 2.4.0).
- O Atualizar (2.5.0+) baixa as partes só quando `binHash` difere do registrado em `bin/pacote.json`, confere a sequência e grava tudo. Sem biblioteca no pacote, `bin/` é preservado.
- Quem estiver na 2.4.0 recebe primeiro os scripts (o Atualizar antigo não conhece `bin/`); clicando de novo em Atualizar, já na 2.5.0, vem a biblioteca C#.
- O instalador (.zip) inclui `bin/` e o `bin/pacote.json`.

## Critérios para migrar uma ferramenta existente (avisar a FJA caso a caso)
Recomendar a migração quando aparecer pelo menos um destes:
- **Monitoramento em tempo real** (`IUpdater`) — ex.: Verificar alterações dos links avisando na hora.
- **Janela que fica aberta** ao lado do modelo, ou **painel encaixável**.
- **Desempenho**: rotina que demora perceptivelmente em modelos reais (muitos elementos ou links).
- **Lógica grande e em crescimento** (o próximo "Sincronizar tipos" já nasce em C#).
- **Biblioteca externa** que o IronPython não usa bem (rede moderna, JSON grande, PDF, Excel).
- A ferramenta vai ser **reescrita de qualquer forma** por mudança grande de escopo.
Não migrar só por migrar: legendas, Renomear e utilitários pequenos podem ficar em Python enquanto funcionarem.

## Ferramentas já em C#
| Botão | Comando | Desde |
|---|---|---|
| Suíte › Sobre | `Sobre` | 2.5.0 |
| Pontos › Codificação | `CodificacaoPontos` (`Legendas/Codificacao.cs`, `JanelaCodificacao.cs`) | 2.8.0 (10/11/2026) |
| Pontos › Legendas (antes "Prévia LOG") | `FerramentaLegendas` (`Legendas/*.cs`; `PreviaLegendaLOG` = mesmo comando) | 2.6.0; ferramenta única desde 2.7.0 (10/11/2026) |
| Símbolos e Detalhes › Família do desenho | `FamiliaDoDesenho` (`Detalhes/*.cs`; ver `docs/familia-do-desenho.md`) | 2.15.0 (12/11/2026) |
| Links › Copiar do link, Verificar alterações, Reconectar | `CopiarDoLink`, `VerificarAlteracoes`, `Reconectar` (`Links/*.cs`; ver `docs/revit-links.md`) | 2.16.0 (12/11/2026; migrados do Python a pedido da FJA, com o módulo Encaminhamentos) |
| Cotagem › Cotar vistas | `CotarVistas` (`Cotagem/*.cs`; ver `docs/cotagem.md`) | 2.18.0 (13/11/2026); fachada 2.20.0 |
| Cotagem › Trocar lado | `TrocarLadoIndicacao` (`Cotagem/JanelaTrocarLado.cs`; ver `docs/cotagem.md`) | 2.19.0 (13/11/2026); janela 2.21.0 |

### Prévia LOG (2.6.0)
- Migrada em 10/11/2026 a pedido da FJA (janela elaborada e lógica em crescimento).
- `Legendas/Coleta.cs` (itens e cor do modelo), `Cores.cs`, `Simbolos.cs` (famílias (LEG), anotações aninhadas, desenho da planta, redimensionamento pelo DWG), `Montagem.cs` (a legenda, linhas, margens, ajustes gravados), `CorSistema.cs` (cor da disciplina no projeto do sistema: banco com o login do `%APPDATA%\FJA\nuvem.json`, senão a cópia do G:), `JanelaPrevia.cs` (janela montada em código, estilo lido de `lib/fja_estilo.xaml`).
- Compatível com o Python: mesmo esquema de marca, mesma entidade de ajustes (GUID derivado do da disciplina) — o botão **Legenda LOG** (Python) usa a ordem, os textos e os itens acrescentados gravados pela Prévia.
- `fja_dotnet.executar` passa a pasta da extensão em `AppDomain` (`FJA.Extensao`): estilo e log de tempos (`REVIT\pyRevit\logs\previa-legenda-tempos.txt`).
- Escala dos símbolos (2.6.2): a primeira vez, a família (LEG) vira DWG (exporta 1:1 e importa); depois, só o **fator de escala** do tipo do DWG dentro da família muda (`Simbolos.Reescalar`), sem exportar de novo. Erros "Line is too short" do DWG reduzido são resolvidos apagando os trechos minúsculos (`ResolverFalhas`).
- Preferências de cor por pessoa: `%APPDATA%\FJA\legenda-previa.json`.
- **Ferramenta única (2.7.0)**: seletor de disciplina (`Disciplina.Todas`: LOG, AUT, SON), lista das legendas da disciplina (`Montagem.ListarLegendas`: vistas com os ajustes gravados, com o nome padrão ou com textos marcados) e **+ Nova legenda** (outra vista, cópia da legenda base); cada legenda guarda os seus itens, ordem, textos, extras e opções (largura, linhas, margens, passo, cor) na entidade de ajustes (`opcoes` no JSON). Gravar mantém a janela aberta. ELE e LUM entram na ferramenta em 2.10.0 (ver abaixo).
- **Variações** (2.9.0): na coleta, para cada tipo, procura parâmetros Sim/Não de instância da família (não do Revit) que mudam entre as instâncias e são calculados por fórmula ou se chamam Baixa/Média/Alta (`Coleta.ParametrosDeVariacao`).
  - Cada combinação de valores vira um item (`Item.Variante`/`VarKey`), com texto "NOME – BAIXA".
  - Cada variação ganha uma família de legenda própria, `(DISC) (LEG) NOME - BAIXA`, montada com a instância representante dela, então as anotações aninhadas certas ficam ligadas.
  - A escala e os ajustes são por variação (`Item.ChaveLeg`).
- Para outra disciplina do mesmo tipo: nova `Disciplina` em `Legendas/Base.cs` e incluir em `Todas`.

## Situação em 09/11/2026
- Compilação no GitHub testada (R2024 e R2027, sem avisos).
- Testado em Python: pacote dividido em partes, aplicação com e sem `bin/`, preservação de `bin/`.
- **Testado no Revit 2027 pelo conector (09/11, ~15h10)**, com uma cópia da DLL em `%LOCALAPPDATA%\fja-teste-dotnet`: versão 2.5.0 / R2027, comando Sobre encontrado, carregamento pela memória (sem `Location`), cache reaproveitado, DLL apagada e substituída com o Revit aberto e versão nova carregada no clique seguinte, comando inexistente devolvendo erro sem derrubar nada. A janela do Sobre não foi aberta pelo conector (diálogo modal).
- **Falta**: clicar em Suíte › Sobre no Revit 2027 e no 2024 depois de atualizar a extensão para a 2.5.0, e a ida e volta do Atualizar com a biblioteca C#.
- Teste pelo conector: o conector lê o código como Latin-1 (acentos chegam trocados) — usar só ASCII no código enviado; e fechar arquivos gravados (`with`) antes de importar.

## ELE e LUM na Legendas (2.10.0, 10/11/2026)
- **Seleção por categoria** (`Disciplina.Categorias`): entram os tipos com a sigla da disciplina e, também, os das categorias dela sem sigla nenhuma (os com a sigla de outra disciplina ficam fora). LUM: luminárias (Lighting Fixtures) e `(LUM)`. ELE: categorias de pontos (Electrical Fixtures, Lighting Devices, Communication, Telephone, Data, Security, Fire Alarm, Nurse Call) e Electrical Equipment.
- **ELE** (`Legendas/Eletrica.cs`, mesmas regras do botão antigo): pontos de parede por tipo + altura ("Elevação do ponto", ao cm; texto "…, A 0,30M DO PISO"), pontos de piso (família com "PISO") e caixas de passagem sem altura, quadros (Electrical Equipment com QUADRO/PAINEL/PANEL/QDC/QGBT) pela representação da família na planta. Ordem: parede, piso, caixas, quadros; tomadas, tomada + interruptor, interruptores, outros. O "a. " do início do nome do tipo sai do texto. O desenho de cada altura usa a variação Baixa/Média/Alta da instância (`Item.VarLeg`): uma família (LEG) por variação, não por altura.
- **Símbolos fixos ELE** (`Disciplina.Fixos`): eletroduto que sobe e que desce (família "Eletroduto que sobe, desce ou passa" do projeto, se houver), pelo teto, pelo piso e eletrocalha. O que não existe vira a anotação `(ELE) (FIX) RÓTULO`, criada ao abrir a disciplina. Entram em toda legenda que ainda não gravou a lista de acrescentados e, daí em diante, funcionam como os acrescentados à mão (tirar, reordenar, escala, texto). Nas `(FIX)` a pintura muda só as linhas (o preenchimento da eletrocalha fica azul-claro).
- **Marca ELE** = a do botão em Python (`FJA_LegendaELE`): a ferramenta substitui a legenda feita por ele. **LUM**: na primeira gravação numa vista feita pelo botão antigo (sem ajustes gravados), apaga os textos "SIGLA.NN - …", as linhas magenta e o título antigo com o sublinhado.
- **Codificação**: LUM entra (LUM.01…; códigos antigos como ARA.01 aparecem como "fora do padrão" até renumerar). ELE fica fora até a FJA decidir os códigos.

## Seletor de cor (2.11.0, 12/11/2026)
- `Nucleo/SeletorCor.cs`: o mesmo seletor do Sistema FJA, para qualquer ferramenta (`SeletorCor.Escolher(dono, corInicial, títuloDoGrupo, amostras)`). Área de cor e matiz, HEX/RGB, conta-gotas (foto da tela mostrada por cima de tudo numa janela própria; o clique lê a cor da foto; Esc ou botão direito cancela; o seletor nunca é escondido — esconder e mostrar a janela modal derrubava o Revit), amostras do chamador, padrão FJA, neutras, recentes (`%APPDATA%\FJA\cores-recentes.txt`) e aviso de contraste com fundo branco.
- Legendas: substitui a caixa de cores do Windows; o grupo "Desta legenda" traz a cor do modelo, a da disciplina no Sistema FJA e a última escolhida.

## Variações pelo que o símbolo mostra (2.12.0, 12/11/2026) — LOG, AUT, SON, LUM
- Regra única, sem olhar nome de família nem de parâmetro: num tipo, as instâncias se separam pelos parâmetros Sim/Não que **ligam ou desligam a visibilidade de algo dentro da família** (anotação aninhada, linha simbólica, região…) e que têm valores diferentes entre as instâncias. Cada combinação é um desenho diferente na vista e vira uma linha da legenda; o desenho da família (LEG) segue essa combinação (`VarLeg`). Os parâmetros saem de `Simbolos.ParametrosDeVisibilidade` (abre a família só quando algum Sim/Não muda entre as instâncias). Família que não abre: regra antiga (fórmula ou nome Baixa/Média/Alta).
- Texto: se as alturas das instâncias (`Eletrica.AlturaGeral`: "Elevação do ponto" ou a elevação em relação ao nível) separam as variações do tipo sem sobreposição, o texto é a altura ("…, A 0,30M DO PISO", "A 0,90M E 1,10M DO PISO"); senão, os parâmetros ligados ("… – MÉDIA"; nenhum ligado = "PADRÃO"). Tipo com um desenho só: uma linha, sem sufixo.
- Substitui a 2.11.1 (altura por nome de família) e a junção por desenho da 2.9.3 (a geometria da instância não enxerga as anotações aninhadas).
- **Família (LEG) por desenho** (2.12.2): antes era uma por família (ou por variação), e tipos diferentes da mesma família com símbolos diferentes (ex.: tomada "baixa" e "média") saíam com o mesmo desenho. Agora, entre os itens da mesma família, o desenho é a lista dos parâmetros de visibilidade ligados no item (tipo ou instância): desenhos diferentes ganham `(SIGLA) (LEG) NOME - PARÂMETROS`, iguais dividem a mesma família.

