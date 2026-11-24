# Builds a signed release APK for Flowza (Android).   Usage:
#   .\make_release_android.ps1 -Version 1.0.0
#
# First run creates flowza.keystore next to this script if it doesn't exist yet - KEEP THAT FILE
# and its password safe. Every future release must be signed with the SAME keystore, or Android
# will refuse to install the update over the old one ("package conflicts with an existing package").
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$KeystorePath = "flowza.keystore",
    [string]$KeyAlias = "flowza"
)
$ErrorActionPreference = "Stop"

# version must be numeric for Android's versionCode; keep a running build number in version.txt
$buildNumber = if (Test-Path version.txt) { [int](Get-Content version.txt) + 1 } else { 1 }
Set-Content version.txt $buildNumber

(Get-Content FlowzaMobile.csproj) `
    -replace '<ApplicationDisplayVersion>.*?</ApplicationDisplayVersion>', "<ApplicationDisplayVersion>$Version</ApplicationDisplayVersion>" `
    -replace '<ApplicationVersion>.*?</ApplicationVersion>', "<ApplicationVersion>$buildNumber</ApplicationVersion>" `
    | Set-Content FlowzaMobile.csproj -Encoding UTF8

# password: take it from an env var if you've set one (CI-friendly), otherwise ask once per session
if (-not $env:FLOWZA_KEYSTORE_PASS) {
    $secure = Read-Host "Keystore password" -AsSecureString
    $env:FLOWZA_KEYSTORE_PASS = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
$pass = $env:FLOWZA_KEYSTORE_PASS

if (-not (Test-Path $KeystorePath)) {
    Write-Host "No keystore found at $KeystorePath - creating a new one. BACK THIS FILE UP." -ForegroundColor Yellow
    keytool -genkeypair -v -keystore $KeystorePath -alias $KeyAlias -keyalg RSA -keysize 2048 -validity 10000 `
        -storepass $pass -keypass $pass -dname "CN=Flowza, OU=Crexx, O=Flowza, L=, S=, C=DE"
}

dotnet publish -f net10.0-android -c Release `
    -p:AndroidPackageFormat=apk `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore=$KeystorePath `
    -p:AndroidSigningKeyAlias=$KeyAlias `
    -p:AndroidSigningKeyPass=$pass `
    -p:AndroidSigningStorePass=$pass

$apk = Get-ChildItem -Recurse -Filter "*-Signed.apk" "bin\Release\net10.0-android\publish" | Select-Object -First 1
if (-not $apk) { throw "Build finished but no signed APK was found under bin\Release\net10.0-android\publish - check the dotnet publish output above for errors." }

$hash = (Get-FileHash $apk.FullName -Algorithm SHA256).Hash.ToLower()
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "APK:      $($apk.FullName)"
Write-Host "Version:  $Version (build $buildNumber)"
Write-Host "SHA256:   $hash"
Write-Host ""
Write-Host "Drop that .apk in your Discord server's download channel - on Android, 'unknown sources'" -ForegroundColor Cyan
Write-Host "has to be allowed once before it'll install (Android asks for that itself on first install)."
