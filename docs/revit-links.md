# Ferramentas FJA › Links (Copiar do link, Verificar alterações, Reconectar)

Em C# desde a 2.16.0 (12/11/2026): `revit/dotnet/FJA.Revit/Links/` (`Base.cs`, `Motor.cs`, `Reconexao.cs`, `JanelasLinks.cs`) e comandos `CopiarDoLink`, `VerificarAlteracoes`, `Reconectar`. Até a 2.15 eram em Python (`lib/fja_links.py`, mantido só como referência).

## Módulos (`lib/fja_links_modulos.json`)
- `tipo: "pecas"` — **Equipamentos**: mobiliário, marcenaria, equipamentos, aparelhos sanitários, luminárias, tomadas e demais dispositivos, sprinklers, terminais de ar.
- `tipo: "redes"` — **Encaminhamentos**: tubos, tubos flexíveis, conexões e acessórios de tubulação; dutos, dutos flexíveis, conexões e acessórios; eletrocalhas, eletrodutos e conexões.
- Estrutura e Arquitetura: `ativo: false` (em breve). Cada categoria pertence a um módulo só. `parametros`: acompanhados pela verificação (padrão Marca e Comentários).

## Registro
- Extensible Storage `FJA_CopiaLink` (GUID `5b1e9c47-2d8a-4f63-9e05-c7a4d1b83f26`), o mesmo do Python: as cópias antigas continuam reconhecidas.
- `foto` (JSON, pés, coordenadas do link): família, tipo, nível, parâmetros `p`, posição do link `lt`, posição da cópia `c`. Peças: `o/x/z`, `esp`, `face`. Trechos: pontas `e`, dimensão `dim` (RBS_CALCULATED_SIZE), sistema `sis`, `k: "rede"`.

## Copiar
- `ElementTransformUtils.CopyElements` com a posição da instância do link. Lotes de 1000: o que é copiado junto chega ligado. Lote recusado é dividido ao meio até isolar o que falha.
- O isolamento vem sozinho com o tubo/duto (incluir à parte duplica — testado).
- Face based vêm sem hospedeiro, no mesmo lugar; hospedados em parede/forro/piso não copiam.
- Janela: link, módulo, filtros (sistema, categoria, nível, situação, busca), workset, "Escolher no modelo…" (com "Escolher a rede conectada": expande pelos conectores do link), "Reconectar depois de copiar".

## Reconectar
1. **Como no link**: para cada ligação física da origem no link cujas duas pontas têm cópia, liga os conectores correspondentes das cópias (mesmo `Connector.Id`; senão o mais próximo), se estiverem no mesmo ponto (≤ 1 mm). Funciona com cópias feitas em dias diferentes.
2. **Pontas coincidentes**: conector livre de uma cópia × conector livre de qualquer elemento do modelo (cópia ou não) a ≤ 1 mm, direções opostas, mesmo domínio, mesma forma e dimensão.
3. **Folgas** (criar luva/curva/redução com aprovação): próxima etapa.
- Relatório: ligações refeitas por nível e pontas que seguem soltas (ligadas no link a algo sem cópia, dimensões diferentes…); os elementos ficam selecionados. Não cria nem move nada.

## Verificar alterações
- Origem agora × foto (alterado no link: posição/pontas, rotação, tipo, espelhamento, nível, dimensão, sistema, parâmetros, link movido) e cópia agora × última sincronização (alterado no projeto; inclui "ligações do link desfeitas").
- Ações: Atualizar pelo link (peças soltas: no lugar; redes e peças ligadas a redes: copia de novo junto, apaga as antigas e reconecta como no link), Aceitar como está, Apagar cópias, Copiar novos (com reconexão), Reconectar, Mostrar no modelo.

## Testes (12/11/2026, Revit 2027, modelos temporários em `%LOCALAPPDATA%\fja-teste-redes`)
- Tubos e cotovelo copiados em duas vezes: Reconectar refez 2 ligações como no link; um tubo do próprio modelo encostado numa ponta foi ligado por ponta coincidente.
- Link alterado (tubo movido 500 mm, diâmetro 50 → 75 mm — o Revit inseriu uma redução —, duto apagado): Verificar mostrou as diferenças, a redução como nova e o duto como apagado; Atualizar + Copiar novos + Reconectar deixaram tudo "Igual" e ligado.
- Equipamentos em C#: cópia exata (0,0 mm, 0,0°); cópia mexida no projeto (400 mm, 10°) voltou ao lugar pelo Atualizar.
- Janelas montadas pelo conector (sem clique). Falta: uso por uma pessoa num modelo real; Revit 2024.
