<#
.SYNOPSIS
    Compila windows-app SIN el .NET SDK, usando el compilador C# (Roslyn) de
    Visual Studio Build Tools y los ensamblados del runtime .NET 8 instalado.

.DESCRIPTION
    Alternativa provisional mientras no esté instalado el .NET SDK. La compilación
    oficial sigue siendo la de los .csproj (dotnet build / dotnet test). No descarga
    ni instala nada. La salida queda en windows-app/bin/nosdk (ignorada por Git).

.EXAMPLE
    .\windows-app\tools\build-nosdk.ps1 -Test
    .\windows-app\tools\build-nosdk.ps1 -Run
#>
param(
    [switch]$Test,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$out = Join-Path $appRoot 'bin\nosdk'

function Find-Csc {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
        if ($found) { return $found }
    }
    throw 'No se encontró el compilador Roslyn (csc.exe) de Visual Studio Build Tools.'
}

function Find-Framework([string]$name) {
    $base = Join-Path $env:ProgramFiles "dotnet\shared\$name"
    $dir = Get-ChildItem $base -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '8.*' } |
        Sort-Object { [version]($_.Name -replace '-.*$', '') } -Descending |
        Select-Object -First 1
    if (-not $dir) { throw "No se encontró el runtime $name 8.x en $base." }
    return $dir.FullName
}

function Get-ManagedAssemblies([string]$dir) {
    Get-ChildItem $dir -Filter *.dll | Where-Object {
        try { [void][Reflection.AssemblyName]::GetAssemblyName($_.FullName); $true } catch { $false }
    } | ForEach-Object { $_.FullName }
}

function Write-RuntimeConfig([string]$path, [string[]]$frameworks) {
    $items = ($frameworks | ForEach-Object { "      { `"name`": `"$_`", `"version`": `"8.0.0`" }" }) -join ",`r`n"
    $json = "{`r`n  `"runtimeOptions`": {`r`n    `"tfm`": `"net8.0`",`r`n    `"frameworks`": [`r`n$items`r`n    ]`r`n  }`r`n}`r`n"
    [IO.File]::WriteAllText($path, $json, (New-Object Text.UTF8Encoding $false))
}

function Invoke-Csc([string]$name, [string]$target, [string]$sourceDir, [string[]]$references) {
    $sources = Get-ChildItem (Join-Path $appRoot $sourceDir) -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | ForEach-Object { $_.FullName }
    $rsp = Join-Path $out "$name.rsp"
    $lines = @(
        '/nologo', '/nostdlib+', "/target:$target", '/langversion:12', '/nullable:enable',
        '/warnaserror+', '/nowarn:1701,1702', '/optimize+', '/deterministic', '/debug:portable',
        "/out:`"$(Join-Path $out "$name.dll")`""
    )
    $lines += $references | ForEach-Object { "/reference:`"$_`"" }
    $lines += $sources | ForEach-Object { "`"$_`"" }
    [IO.File]::WriteAllLines($rsp, $lines)
    Write-Host "Compilando $name ($($sources.Count) archivos)..."
    # /noconfig solo es válido en la línea de comandos: evita el csc.rsp de .NET Framework.
    & $script:csc /noconfig "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw "Falló la compilación de $name." }
}

$script:csc = Find-Csc
$netcore = Find-Framework 'Microsoft.NETCore.App'
$desktop = Find-Framework 'Microsoft.WindowsDesktop.App'
New-Item -ItemType Directory -Force $out | Out-Null

$coreRefs = Get-ManagedAssemblies $netcore
$desktopRefs = $coreRefs + (Get-ManagedAssemblies $desktop)
$coreDll = Join-Path $out 'SmartControl.Core.dll'

Invoke-Csc 'SmartControl.Core' 'library' 'src\SmartControl.Core' $coreRefs
Invoke-Csc 'SmartControl' 'winexe' 'src\SmartControl.Desktop' ($desktopRefs + $coreDll)
Invoke-Csc 'SmartControl.Core.Tests' 'exe' 'tests\SmartControl.Core.Tests' ($coreRefs + $coreDll)
Invoke-Csc 'SmartControl.Desktop.Tests' 'exe' 'tests\SmartControl.Desktop.Tests' ($desktopRefs + $coreDll + (Join-Path $out 'SmartControl.dll'))

Write-RuntimeConfig (Join-Path $out 'SmartControl.runtimeconfig.json') @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')
Write-RuntimeConfig (Join-Path $out 'SmartControl.Core.Tests.runtimeconfig.json') @('Microsoft.NETCore.App')
Write-RuntimeConfig (Join-Path $out 'SmartControl.Desktop.Tests.runtimeconfig.json') @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')
Write-Host "Compilación correcta en $out"

if ($Test) {
    & dotnet (Join-Path $out 'SmartControl.Core.Tests.dll')
    $coreFailed = $LASTEXITCODE -ne 0
    & dotnet (Join-Path $out 'SmartControl.Desktop.Tests.dll')
    if ($coreFailed -or $LASTEXITCODE -ne 0) { throw 'Hay pruebas fallidas.' }
}
if ($Run) {
    Start-Process dotnet -ArgumentList "`"$(Join-Path $out 'SmartControl.dll')`""
}
