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

## Situação em 09/11/2026
- Compilação no GitHub testada (R2024 e R2027, sem avisos).
- Testado em Python: pacote dividido em partes, aplicação com e sem `bin/`, preservação de `bin/`.
- **Falta testar no Revit**: o botão Sobre (2024 e 2027) e a ida e volta do Atualizar com a biblioteca C#. Na tentativa pelo conector, o Revit estava ocupado por outra sessão e as respostas vieram trocadas.
