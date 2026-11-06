# Instalador da extensao Ferramentas FJA (pyRevit) - versao __VERSAO__
# Vai dentro do instalar.cmd gerado pelo Sistema FJA (Biblioteca > Ferramentas > Extensao do Revit).
# Textos sem acento de proposito: o console do Windows nem sempre mostra acentos.
#
# 1. Copia Ferramentas-FJA.extension (ao lado do instalar.cmd) para a pasta compartilhada do G:
#    (ou, sem o G:, para %APPDATA%\pyRevit\Extensions). Nao substitui uma versao mais nova.
# 2. Cadastra a pasta compartilhada no pyRevit deste usuario (pyRevit_config.ini, [core] userextensions).
# 3. Tira a copia local antiga de %APPDATA%\pyRevit\Extensions, para a aba nao aparecer duas vezes.
# Variaveis so para teste: FJA_TESTE_REVIT (pasta REVIT do G:), FJA_TESTE_APPDATA.
$ErrorActionPreference = 'Stop'
$VERSAO = '__VERSAO__'
$NOME = 'Ferramentas-FJA.extension'
function Ok($t) { Write-Host ('  ' + $t) -ForegroundColor Green }
function Info($t) { Write-Host ('  ' + $t) }
function Aviso($t) { Write-Host ('  ' + $t) -ForegroundColor Yellow }
function Versao($pasta) {
  try { return [version]((Get-Content -Raw -LiteralPath (Join-Path $pasta 'extension.json') | ConvertFrom-Json).version) } catch { return $null }
}
try {
  Write-Host ''
  Write-Host ('  Ferramentas FJA (pyRevit) - instalador da versao ' + $VERSAO) -ForegroundColor Cyan
  Write-Host ''
  $aqui = Split-Path -Parent $env:FJA_SELF
  $origem = Join-Path $aqui $NOME
  if (-not (Test-Path -LiteralPath (Join-Path $origem 'extension.json'))) {
    throw ('nao encontrei a pasta ' + $NOME + ' ao lado do instalar.cmd. Extraia o .zip inteiro (botao direito > Extrair tudo) e rode o instalar.cmd da pasta extraida.')
  }
  $appdata = if ($env:FJA_TESTE_APPDATA) { $env:FJA_TESTE_APPDATA } else { $env:APPDATA }
  $revitG = if ($env:FJA_TESTE_REVIT) { $env:FJA_TESTE_REVIT } else { 'G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT' }
  $pyr = Join-Path $appdata 'pyRevit'
  $local = Join-Path $pyr 'Extensions'
  $temG = Test-Path -LiteralPath $revitG
  if ($temG) { $pastaExt = Join-Path $revitG 'pyRevit' } else { $pastaExt = $local }
  $destino = Join-Path $pastaExt $NOME

  if (-not (Test-Path -LiteralPath $pyr)) {
    Aviso 'O pyRevit nao parece instalado para este usuario (pasta %APPDATA%\pyRevit nao existe).'
    Aviso 'Instale o pyRevit (https://pyrevitlabs.io) e rode este instalador de novo. Seguindo assim mesmo...'
    New-Item -ItemType Directory -Force -Path $pyr | Out-Null
  }
  if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    Aviso 'O Revit esta aberto: depois de instalar, clique em pyRevit > Reload (ou reabra o Revit).'
  }

  # 1. copia (espelha a pasta, sem apagar nada fora dela)
  $vNova = Versao $origem
  $vAtual = Versao $destino
  if ($vAtual -and $vNova -and $vAtual -gt $vNova) {
    Aviso ('A pasta ja tem a versao ' + $vAtual + ', mais nova que esta (' + $vNova + '): os arquivos nao foram substituidos.')
  } else {
    New-Item -ItemType Directory -Force -Path $pastaExt | Out-Null
    $null = & robocopy $origem $destino /MIR /XF *.pyc /R:2 /W:2 /NJH /NJS /NFL /NDL /NP
    if ($LASTEXITCODE -ge 8) { throw ('falha ao copiar os arquivos (robocopy ' + $LASTEXITCODE + ') para ' + $destino) }
    if (-not $vAtual) { Ok ('Extensao ' + $vNova + ' copiada para:') }
    elseif ($vAtual -eq $vNova) { Ok ('Extensao ' + $vNova + ' conferida (arquivos atualizados) em:') }
    else { Ok ('Extensao atualizada de ' + $vAtual + ' para ' + $vNova + ' em:') }
    Info $destino
  }

  if ($temG) {
    # 2. cadastrar a pasta compartilhada no pyRevit deste usuario
    $ini = Join-Path $pyr 'pyRevit_config.ini'
    $linhas = New-Object System.Collections.Generic.List[string]
    if (Test-Path -LiteralPath $ini) { foreach ($l in [IO.File]::ReadAllLines($ini)) { $linhas.Add($l) } }
    $iCore = -1; $iUser = -1; $iFim = $linhas.Count
    for ($i = 0; $i -lt $linhas.Count; $i++) {
      $t = $linhas[$i].Trim()
      if ($t -match '^\[.+\]$') {
        if ($iCore -ge 0 -and $iFim -eq $linhas.Count -and $i -gt $iCore) { $iFim = $i }
        if ($t -eq '[core]') { $iCore = $i }
      } elseif ($iCore -ge 0 -and $iFim -eq $linhas.Count -and $t -match '^userextensions\s*=') { $iUser = $i }
    }
    $lista = @()
    if ($iUser -ge 0) {
      $val = ($linhas[$iUser] -split '=', 2)[1].Trim()
      try { $lista = @($val | ConvertFrom-Json) } catch { $lista = @() }
    }
    if ($lista | Where-Object { $_ -and ($_.TrimEnd('\') -ieq $pastaExt.TrimEnd('\')) }) {
      Ok 'O pyRevit deste usuario ja le a pasta compartilhada.'
    } else {
      $lista = @($lista | Where-Object { $_ }) + @($pastaExt)
      $nova = 'userextensions = [' + (($lista | ForEach-Object { '"' + ($_ -replace '\\', '\\') + '"' }) -join ', ') + ']'
      if (Test-Path -LiteralPath $ini) { Copy-Item -LiteralPath $ini -Destination ($ini + '.antes-fja') -Force }
      if ($iUser -ge 0) { $linhas[$iUser] = $nova }
      elseif ($iCore -ge 0) { $linhas.Insert($iCore + 1, $nova) }
      else { if ($linhas.Count) { $linhas.Add('') }; $linhas.Add('[core]'); $linhas.Add($nova) }
      [IO.File]::WriteAllLines($ini, $linhas.ToArray())
      Ok 'Pasta compartilhada cadastrada no pyRevit deste usuario (pyRevit_config.ini; copia anterior em .antes-fja).'
    }
    # 3. copia local antiga (aba duplicada)
    $velha = Join-Path $local $NOME
    if (Test-Path -LiteralPath $velha) {
      $bk = Join-Path $pyr '_backup-extensoes'
      New-Item -ItemType Directory -Force -Path $bk | Out-Null
      $alvo = Join-Path $bk ($NOME + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
      Move-Item -LiteralPath $velha -Destination $alvo
      Ok 'A copia local antiga foi movida para pyRevit\_backup-extensoes (a aba nao aparece duas vezes).'
    }
  } else {
    Aviso 'Sem acesso ao G: (Drives compartilhados\BI-BIBLIOTECA): instalado so neste computador.'
    Aviso 'Quando o G: estiver disponivel, rode o instalador de novo para usar a pasta compartilhada.'
  }
  Write-Host ''
  Ok 'Pronto. No Revit: pyRevit > Reload (ou abra o Revit). A aba Ferramentas FJA mostra as ferramentas novas.'
} catch {
  Write-Host ''
  Write-Host ('  Nao foi possivel instalar: ' + $_.Exception.Message) -ForegroundColor Red
}
