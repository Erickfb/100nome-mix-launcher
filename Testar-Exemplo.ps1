#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$arquitetura = if ([Environment]::Is64BitOperatingSystem) { 'Framework64' } else { 'Framework' }
$compilador = Join-Path $env:WINDIR "Microsoft.NET\$arquitetura\v4.0.30319\csc.exe"
if (-not [IO.File]::Exists($compilador)) { throw 'Instale o .NET Framework 4.x para Windows.' }

$fontes = foreach ($nome in 'Cenarios.cs','ConsultaServidor.cs','Exemplo.cs') {
    (Get-Item -LiteralPath (Join-Path $PSScriptRoot $nome)).FullName
}
$pasta = Join-Path ([IO.Path]::GetTempPath()) ('exemplo-a2s-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $pasta -ErrorAction Stop)
$exe = Join-Path $pasta 'Exemplo.exe'
try {
    & $compilador /nologo /noconfig /target:exe /codepage:65001 /reference:System.dll /reference:System.Core.dll "/out:$exe" @fontes
    if ($LASTEXITCODE -ne 0) { throw 'A compilação do exemplo falhou.' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'A resposta A2S fictícia divergiu do resultado esperado.' }
} finally {
    [IO.File]::Delete($exe)
    [IO.Directory]::Delete($pasta)
}
