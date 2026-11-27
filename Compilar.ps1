#requires -Version 5.1
[CmdletBinding()]
param([string]$Perfil, [string]$Destino)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use o Windows PowerShell 5.1.' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) { throw 'Instale o .NET Framework 4.8 no Windows.' }
$configurado = $PSBoundParameters.ContainsKey('Perfil')
if (-not $Destino) {
    $nome = if ($configurado) { 'Mix comunidade.exe' } else { 'Mix demonstracao.exe' }
    $Destino = Join-Path $PSScriptRoot $nome
}
$provedor = $null
$unidade = $null
$saida = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destino,[ref]$provedor,[ref]$unidade)
if ($provedor.Name -ne 'FileSystem' -or -not [IO.Directory]::Exists([IO.Path]::GetDirectoryName($saida))) {
    throw 'Escolha uma pasta de arquivos que já exista para o EXE.'
}
if ([IO.Path]::GetExtension($saida) -ine '.exe' -or (Test-Path -LiteralPath $saida)) {
    throw 'Escolha um nome de EXE que ainda não exista.'
}
$arquivoPerfil = if ($configurado) { (Get-Item -LiteralPath $Perfil).FullName } else { Join-Path $PSScriptRoot 'perfil.exemplo.json' }
$config = Get-Content -LiteralPath $arquivoPerfil -Raw -Encoding UTF8 | ConvertFrom-Json
$campos = @('DominioPrincipal','DominioTerceiro','EnderecoLocalCadastro','PortaCadastro','SteamIdServidor',
    'CanalDiscord','PinTlsCadastro','ModuloEmissor','PastaDados','ChaveLocalPC')
if (Compare-Object -CaseSensitive ($campos | Sort-Object) ($config.PSObject.Properties.Name | Sort-Object)) {
    throw 'Confira os nomes dos campos em perfil.exemplo.json.'
}
foreach ($campo in $campos) {
    if ($config.$campo -isnot [string] -or $config.$campo -match '[\p{C}\p{Zl}\p{Zp}]') { throw "Valor inválido em $campo." }
}
foreach ($hostJogo in $config.DominioPrincipal,$config.DominioTerceiro) {
    if ([Uri]::CheckHostName($hostJogo) -ne 'Dns') { throw 'Use somente o nome DNS em cada domínio.' }
}
if ($config.EnderecoLocalCadastro) {
    $ip = $null
    if (-not [Net.IPAddress]::TryParse($config.EnderecoLocalCadastro,[ref]$ip) -or
        $ip.AddressFamily -ne 'InterNetwork' -or $ip.ToString() -ne $config.EnderecoLocalCadastro) { throw 'Confira o IPv4 local do cadastro.' }
}
$discord = $null
if (-not [Uri]::TryCreate($config.CanalDiscord,'Absolute',[ref]$discord) -or $discord.Scheme -ne 'https' -or
    $discord.UserInfo -or $discord.Query -or $discord.Fragment) {
    throw 'Informe o link HTTPS da sua comunidade.'
}
if ($config.PastaDados -notmatch '^[A-Za-z][A-Za-z0-9_-]{0,63}$' -or
    $config.PastaDados -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$' -or
    $config.ChaveLocalPC -notmatch '^[A-Za-z][A-Za-z0-9_.-]{0,79}$' -or $config.ChaveLocalPC.Contains('..')) {
    throw 'Confira os nomes do perfil e da chave do PC.'
}
$steamId = [uint64]0
if (-not [uint64]::TryParse($config.SteamIdServidor,[ref]$steamId) -or $steamId -eq 0) { throw 'Confira o ID do servidor.' }
$portaCadastro = [uint16]0
if (-not [uint16]::TryParse($config.PortaCadastro,[ref]$portaCadastro) -or $portaCadastro -eq 0) { throw 'Confira a porta HTTPS do cadastro.' }
if ($configurado) {
    if ([Convert]::FromBase64String($config.PinTlsCadastro).Length -ne 32 -or
        [Convert]::FromBase64String($config.ModuloEmissor).Length -notin (256,384,512)) {
        throw 'Defina o pin público TLS e o módulo RSA público do emissor da sua comunidade.'
    }
}
$versao = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'versao.txt')).Trim()
if ($versao -notmatch '^\d+\.\d+\.\d+$') { throw 'A versão deve ter três números.' }
$fontes = @('Atualizacao','AtualizacaoPacote','AutorizacaoRota','CadastroAutomatico',
    'Cenarios','Central','ConsultaServidor','Convite','EntradasMix','GuiaPublico','JanelaAtivacao',
    'JanelaConvite','Launcher','LicencaPC','MuralComunidade','NoticiasSteam','PatentePessoal','PatentesDoMix','PatentesNaCentral')
if (-not $configurado) { $fontes += 'Previa' }
$fontes = @($fontes | ForEach-Object { (Get-Item -LiteralPath (Join-Path $PSScriptRoot ($_ + '.cs'))).FullName })

$pasta = Join-Path ([IO.Path]::GetDirectoryName($saida)) ('.mix-build-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $pasta)
try {
    $versaoFonte = Join-Path $pasta 'Projeto.g.cs'
    $linhas = @('using System.Reflection;', ('[assembly: AssemblyVersion("{0}.0")]' -f $versao),
        ('[assembly: AssemblyFileVersion("{0}.0")]' -f $versao), ('[assembly: AssemblyInformationalVersion("{0}")]' -f $versao),
        'internal static class PerfilInstancia', '{', ('    internal const ulong SteamIdServidor = {0}UL;' -f $steamId),
        ('    internal const int PortaCadastro = {0};' -f $portaCadastro))
    foreach ($campo in $campos) {
        if ($campo -in 'SteamIdServidor','PortaCadastro') { continue }
        $valor = $config.$campo
        if ($campo -eq 'PastaDados') { $valor = 'MixComunidades\' + $valor }
        if ($campo -eq 'ChaveLocalPC') { $valor = 'MixComunidades.' + $valor }
        $literal = $valor.Replace('\','\\').Replace('"','\"')
        $linhas += '    internal const string ' + $campo + ' = "' + $literal + '";'
    }
    [IO.File]::WriteAllLines($versaoFonte,[string[]]($linhas + '}'),[Text.UTF8Encoding]::new($true))
    $temporario = Join-Path $pasta 'Mix.exe'
    $entrada = if ($configurado) { 'Convite' } else { 'Launcher' }
    $interface = Join-Path $PSScriptRoot 'Interface.xaml'
    $referencias = @('WPF\PresentationFramework','WPF\PresentationCore','WPF\WindowsBase','System.Xaml','System.Xml','System.Runtime.Serialization') |
        ForEach-Object { '/r:' + (Join-Path $framework ($_ + '.dll')) }
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 /optimize+ "/main:$entrada" "/out:$temporario" @referencias "/resource:$interface,interface" @fontes $versaoFonte
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação do cliente.' }
    [IO.File]::Move($temporario,$saida)
    $saida
} finally {
    foreach ($arquivo in 'Projeto.g.cs','Mix.exe') {
        [IO.File]::Delete((Join-Path $pasta $arquivo))
    }
    [IO.Directory]::Delete($pasta)
}
