# Tipos › Componentes (painel encaixável)

Painel em C# desde a extensão 2.28.0 (19/11/2026), a pedido da FJA: no lugar da janela em Python que trabalhava uma família por vez (retirada em 2.29.0), uma árvore com todas as famílias carregáveis do modelo, aberta ao lado do modelo (em aba com Propriedades).

## Decisões da FJA (19/11/2026)
- À esquerda (no painel), uma **árvore Categoria › Família › Tipo** com as famílias carregáveis pertinentes à conexão com o Sistema FJA; **todas as famílias exibidas por padrão**.
- **Busca** para filtrar.
- **Verde** nos tipos sincronizados com o sistema (ligados a um keynote cadastrado no projeto).
- Botão **Sincronizar**.
- **Painel encaixável**, como a barra de Propriedades; migrar para C#.

## Como funciona
- **Abrir:** Tipos › Componentes. O painel fica encaixado em aba com Propriedades (pode ser solto ou levado para outro lugar; o Revit lembra). Na primeira vez depois de atualizar a suíte, **fechar e abrir o Revit** (o Revit só registra painéis na abertura). Se o painel ainda não foi criado (o Atualizar recarrega o pyRevit com o Revit aberto, e o Revit só cria painéis na abertura), o botão abre o mesmo conteúdo numa **janela** e avisa uma vez (2.29.1).
- **Árvore:** famílias carregáveis de modelo (sem in-place, anotações, itens de detalhe, perfis, massa e carimbos). Categoria e família mostram "sincronizados/total"; categorias abertas e famílias fechadas por padrão (com busca, tudo aberto). Cada tipo tem um ponto colorido, o keynote ao lado e "sem instâncias" quando não está colocado.
- **Cores** (`Coleta.Diagnosticar`, mesma regra do Diagnóstico em Python):
  | Cor | Situação |
  |---|---|
  | verde | ligado a um keynote do projeto e igual ao sistema (Keynote, Type Mark e Descrição) |
  | âmbar | ligado com diferença: o sistema mudou, alguém alterou no Revit (vínculo v2) ou o item está fora da especificação do projeto |
  | azul | aguardando: pedido ainda não processado pelo sistema (ou sistema não lido) |
  | vermelho | o código ligado não existe mais no sistema |
  | cinza | sem ligação |
- **Busca e filtro:** a busca procura em categoria, família, tipo, keynote, Type Mark, descrição e código FJ (todos os termos, sem acento); o filtro mostra Todos, Sincronizados, Com diferença, Sem ligação ou Aguardando/erro.
- **Fonte e projeto:** Oficial (banco do sistema com o login do computador, senão a cópia do G:) ou Desenvolvimento (REVIT\DEV); o projeto vem do código no nome do arquivo (como nas outras ferramentas) e pode ser trocado na lista. Preferências por pessoa em `%APPDATA%\FJA\componentes.json` (fonte, renomear).
- **Detalhes** do nó escolhido (embaixo da árvore): valores no Revit, código, diferenças campo a campo e ações:
  - **Sincronizar** (tipo, família, categoria) e **Sincronizar tudo** (no alto): traz Keynote, Type Mark e Descrição do sistema; antes, mostra a lista do que muda e avisa quando os valores foram alterados no Revit (o sistema prevalece). Item fora da especificação do projeto entra nela com o próximo keynote da sigla (pedido `incluir-no-projeto`, um por item). Opção de renomear os tipos como KEYNOTE - DESCRIÇÃO.
  - **Ligar a um código** (tipo ou família inteira): lista dos itens de elemento/família da biblioteca, com busca (sugere a sigla entre parênteses no nome da família) e o keynote no projeto em verde quando já está na especificação. Grava Keynote, Type Mark, Descrição, Comentários de tipo (= FJ) e o vínculo `FJA_SistemaTipo2`; vários tipos no mesmo código ganham " - nome original" ao renomear.
  - **Selecionar no modelo** (as instâncias) e **Desligar** (apaga o vínculo; os parâmetros ficam).
- **Atualização:** o painel relê o modelo quando muda o documento ativo ou quando famílias, tipos ou instâncias mudam (inclusive pelas gravações do próprio painel), no primeiro momento ocioso com o painel visível. O botão ↻ relê também o sistema.
- Gravações pelo `ExternalEvent` (fila em `PainelComponentes`), uma transação por ação (Ctrl+Z desfaz).

## Montagem técnica
- `Componentes/Registro.cs`: o `startup.py` chama `fja_dotnet.registrar_paineis` → `Entrada.RegistrarPaineis` → `RegisterDockablePane` com uma **moldura** fixa (Border) guardada no AppDomain. O conteúdo (`PainelComponentes`) é montado no primeiro momento ocioso e, quando a DLL muda (Suíte › Atualizar), o botão monta o conteúdo novo dentro da mesma moldura, sem reiniciar o Revit. O painel antigo deixa de responder aos eventos (cada um confere se ainda é o atual).
- `Componentes/Sistema.cs` (`DadosSistema`, porte de `Dados` do `fja_sistema.py`), `Nucleo/Nuvem.cs` (banco: `revitPub`, pedidos pendentes, envio de pedido), `Componentes/Vinculo.cs` (mesmos GUIDs e campos do Python), `Componentes/Coleta.cs` (tipos e diagnóstico), `Componentes/Operacoes.cs` (ligar, sincronizar, desligar).

## Carregar família, código novo e localizar (2.29.0, 19/11/2026)
Com isto o painel faz tudo o que a janela antiga em Python fazia, e o botão **Novo componente** foi retirado.
- **Carregar família…** (no alto): escolhe o .rfa, carrega no modelo (se já existir, recarrega sem sobrescrever os valores dos parâmetros) e deixa a família escolhida e aberta na árvore, para ligar os tipos.
- **Localizar seleção**: mostra na árvore o tipo do elemento selecionado no modelo (substitui o "Selecionar no modelo" da janela antiga).
- **Ligar › Código novo**: sigla (da lista ou **+ Nova sigla…**, com 3 letras e o nome da tabela), descrição e especificação opcional. Reserva o próximo `FJ.SIGLA.NNNN` e o próximo keynote da sigla no projeto e envia o pedido `novo-item` (com `siglaNova` quando a sigla também é nova); os tipos ficam azuis (aguardando) até o sistema processar. Códigos novos ainda pendentes aparecem na lista de códigos existentes, para ligar outros tipos ao mesmo código.
- **Renomear a família como (SIGLA) DESCRIÇÃO** (padrão ligado, por pessoa), ao ligar uma família ou um tipo; não troca se já existir outra família com esse nome.

## Setas ← e → (2.31.0, 19/11/2026)
Pedido da FJA: trazer do sistema ou levar ao sistema, nos tipos escolhidos.
- **Caixas de seleção** em cada tipo, família e categoria (família e categoria marcam os tipos que aparecem na busca e no filtro atuais). A barra abaixo da busca mostra quantos estão marcados e tem **← Do sistema**, **→ Para o sistema** e **Limpar**. Sem tipos marcados, as setas agem no item escolhido na árvore. Os detalhes do tipo também têm as duas setas, com a sugerida em destaque.
- **← Do sistema** = Sincronizar nos tipos escolhidos (Keynote, Type Mark e Descrição do sistema; o sistema é a referência).
- **→ Para o sistema** (decisão da FJA): só para tipos **já no padrão** — ligados a um item da especificação do projeto e sincronizados antes (vínculo v2) — cuja **Descrição foi alterada no Revit** depois da última sincronização (`Coleta.PodeLevar`). Só a Descrição vai: Keynote e Type Mark são numeração do sistema. Envia o pedido `editar-item` só com `{id, desc}` (o sistema registra a descrição como a principal do item central e atualiza a linha do projeto); confirmação antes, avisando que a descrição é do item central e muda em todos os projetos que usam o código. Tipos do mesmo item com descrições diferentes ficam de fora (aviso). O vínculo dos tipos recebe o id do pedido: ficam **aguardando** (azul) até o sistema processar e depois verdes.

## Cabeçalho com ícones e ↻ animado (2.31.1, 23/11/2026)
- **Carregar família** (pasta com +) e **Localizar seleção** (mira) viram ícones no cabeçalho, ao lado do ↻ (a explicação fica na dica). Em 2.31.2, também **Sincronizar tudo** (seta descendo do sistema para o modelo; âmbar quando há diferenças, contagem na dica) e **Reconectar** (elo, manutenção). Todo ícone tem dica.
- Textos explicativos saem do painel e viram dicas: fonte e data de publicação (na lista Oficial/Desenvolvimento), origem do projeto (no nome do projeto), legenda das cores (no resumo), regras do Ligar (no título do bloco), avisos de "fora da especificação" e "alterado no Revit" (na situação do tipo). Ficam visíveis só os erros.
- **↻ gira** enquanto o sistema e o modelo são relidos. A leitura do sistema (rede) passou para segundo plano — a interface não congela e a animação aparece —; ao terminar, o modelo é relido pelo ExternalEvent e o giro para. A leitura automática (primeira abertura, troca de fonte e troca de modelo com dados de mais de 5 minutos) usa o mesmo caminho.
