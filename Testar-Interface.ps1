#requires -Version 5.1
param([Parameter(Mandatory)][string]$Exe, [switch]$SemDesktop)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Use powershell.exe -STA.' }
Add-Type -AssemblyName PresentationFramework
$arquivo = (Get-Item -LiteralPath $Exe).FullName
$app = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($arquivo))
$esperados = 'BouncyCastle.Cryptography.dll','BouncyCastle.LICENSE.md','interface'
if (Compare-Object ($esperados | Sort-Object) ($app.GetManifestResourceNames() | Sort-Object)) {
    throw 'Os recursos embutidos divergem da lista do build público.'
}
$interno = [Reflection.BindingFlags]'Instance,NonPublic'
$tipo = $app.GetType('Launcher',$true)
$launcher = $tipo.GetConstructor($interno,$null,[Type[]]@(),$null).Invoke(@())
$janela = $tipo.GetProperty('Janela',$interno).GetValue($launcher)
try {
    $janela.FindName('AbaComandos').IsChecked = $true
    $janela.FindName('Busca').Text = 'ready'
    $lista = $janela.FindName('ListaComandos')
    if ($lista.Items.Count -lt 1 -or '!ready' -notin $lista.Items.Comando) { throw 'Falha na busca do guia.' }
    $tipo.GetMethod('AlternarCatalogo',$interno).Invoke($launcher,@())
    if ($janela.FindName('CatalogoPatentes').Items.Count -ne 18) { throw 'Falha no catálogo de patentes sem imagens.' }
    $ativacao = $app.GetType('JanelaAtivacao',$true).GetConstructor($interno,$null,[Type[]]@([string]),$null).Invoke(@('PC de demonstração'))
    try { if (!$ativacao.Content) { throw 'A janela de ativação não abriu.' } } finally { $ativacao.Close() }
} finally { $janela.Close() }
if ($app.EntryPoint.DeclaringType.Name -eq 'Launcher') {
    if ($SemDesktop) {
        $previa = $tipo.GetConstructor($interno,$null,[Type[]]@([string]),$null).Invoke(@('Demonstração offline'))
        $guia = $tipo.GetProperty('Janela',$interno).GetValue($previa)
        try {
            if ($guia.FindName('Guia').Visibility -ne 'Visible' -or !$guia.FindName('ListaComandos').Items.Count) {
                throw 'Falha ao carregar a demonstração offline.'
            }
        } finally { $guia.Close() }
        Write-Output 'Demonstração offline e controles WPF validados.'
        return
    }
    $processo = Start-Process -FilePath $arquivo -PassThru
    try {
        $fim = [DateTime]::UtcNow.AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 100
            $processo.Refresh()
        } until ($processo.HasExited -or $processo.MainWindowTitle -eq 'Guia de comandos · demonstração offline' -or [DateTime]::UtcNow -ge $fim)
        if ($processo.HasExited -or $processo.MainWindowTitle -ne 'Guia de comandos · demonstração offline') {
            throw 'O executável de demonstração não abriu o guia.'
        }
    } finally {
        if (!$processo.HasExited) {
            [void]$processo.CloseMainWindow()
            if (!$processo.WaitForExit(5000)) { $processo.Kill() }
        }
        $processo.Dispose()
    }
} elseif ($app.EntryPoint.DeclaringType.Name -ne 'Convite') { throw 'Ponto de entrada desconhecido.' }
Write-Output 'WPF, guia pesquisável, ativação, catálogo de 18 patentes e inventário de recursos conferidos.'
