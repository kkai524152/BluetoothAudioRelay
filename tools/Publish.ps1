[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$CertificateThumbprint,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "BluetoothAudioRelay.csproj"
$testProject = Join-Path $projectRoot "tests\BluetoothAudioRelay.Tests\BluetoothAudioRelay.Tests.csproj"
$projectXml = [xml](Get-Content -LiteralPath $projectFile -Raw)
$version = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "无法从项目文件读取版本号。"
}

function Get-SignToolPath {
    $candidates = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" `
        -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending
    return $candidates | Select-Object -First 1 -ExpandProperty FullName
}

function Invoke-CodeSign([string]$filePath) {
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        return
    }

    $signTool = Get-SignToolPath
    if ([string]::IsNullOrWhiteSpace($signTool)) {
        throw "提供了证书指纹，但未找到 Windows SDK signtool.exe。"
    }

    & $signTool sign /sha1 $CertificateThumbprint /fd SHA256 `
        /tr "http://timestamp.digicert.com" /td SHA256 $filePath
    if ($LASTEXITCODE -ne 0) {
        throw "代码签名失败：$filePath"
    }
}

dotnet test $testProject --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "测试失败，发布已停止。"
}

$targets = @(
    @{ Runtime = "win-x64"; Output = "lightweight" },
    @{ Runtime = "win-arm64"; Output = "lightweight-arm64" }
)

foreach ($target in $targets) {
    $publishDirectory = Join-Path $projectRoot "publish\$($target.Output)"
    dotnet publish $projectFile --configuration $Configuration `
        --runtime $target.Runtime --self-contained false `
        -p:PublishSingleFile=true -p:DebugType=None `
        --output $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "发布失败：$($target.Runtime)"
    }

    $executable = Join-Path $publishDirectory "BluetoothAudioRelay.exe"
    Invoke-CodeSign $executable

    $archive = Join-Path $projectRoot "dist\BluetoothAudioRelay-$($target.Runtime).zip"
    Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archive -Force
}

if (-not $SkipInstaller) {
    $innoCandidates = @(
        (Join-Path $projectRoot ".tools\innosetup6\ISCC.exe"),
        (Join-Path $projectRoot ".tools\innosetup6\tools\ISCC.exe"),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $innoCompiler = $innoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($innoCompiler)) {
        throw "未找到 Inno Setup 6。使用 -SkipInstaller 可只生成 x64/ARM64 压缩包。"
    }

    & $innoCompiler "/DMyAppVersion=$version" (Join-Path $projectRoot "installer\BluetoothAudioRelay.iss")
    if ($LASTEXITCODE -ne 0) {
        throw "安装包构建失败。"
    }

    Invoke-CodeSign (Join-Path $projectRoot "dist\BluetoothAudioRelay-Setup-x64.exe")
}

Write-Host "发布完成：Bluetooth Audio Relay $version"
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    Write-Warning "未提供 CertificateThumbprint，生成物未进行 Authenticode 签名。"
}
