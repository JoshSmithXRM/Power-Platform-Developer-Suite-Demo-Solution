# Generate Strong Name Key for plugin assembly
$snkPath = Join-Path $PSScriptRoot "..\src\Plugins\PPDSDemo.Plugins\PPDSDemo.Plugins.snk"

# Create RSA key pair using RSACryptoServiceProvider (supports ExportCspBlob)
$cspParams = New-Object System.Security.Cryptography.CspParameters
$cspParams.KeyNumber = 2  # Signature key
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider(2048, $cspParams)
$keyBlob = $rsa.ExportCspBlob($true)  # true = include private key

# Write to file
[System.IO.File]::WriteAllBytes($snkPath, $keyBlob)

Write-Host "Generated: $snkPath" -ForegroundColor Green
Write-Host "Key size: $($keyBlob.Length) bytes"

$rsa.Dispose()
