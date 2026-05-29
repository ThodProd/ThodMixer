# Automated script to build, sign and package ThodMixer

$ErrorActionPreference = "Stop"

Write-Host "=== 1. Creating Self-Signed Code Signing Certificate ===" -ForegroundColor Green

# Define certificate details
$certSubject = "CN=ThodMixer,O=ThodMixer,C=RU"
$certFriendlyName = "ThodMixer Code Signing"
$passwordText = "ThodMixerPassword123"
$pfxPath = "ThodMixer_Cert.pfx"

# Create a self-signed certificate in CurrentUser\My
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $certSubject -KeyUsage DigitalSignature -FriendlyName $certFriendlyName -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(5)
Write-Host "Certificate created with thumbprint: $($cert.Thumbprint)" -ForegroundColor Cyan

# Export to PFX
$password = ConvertTo-SecureString $passwordText -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password
Write-Host "Certificate exported to $pfxPath" -ForegroundColor Cyan

Write-Host "=== 2. Trusting Certificate Locally ===" -ForegroundColor Green
# Import to trusted root and trusted publisher to bypass SmartScreen on this machine
$storeRoot = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "CurrentUser")
$storeRoot.Open("ReadWrite")
$storeRoot.Add($cert)
$storeRoot.Close()
Write-Host "Imported to Trusted Root Certification Authorities (CurrentUser)" -ForegroundColor Cyan

$storePub = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPublisher", "CurrentUser")
$storePub.Open("ReadWrite")
$storePub.Add($cert)
$storePub.Close()
Write-Host "Imported to Trusted Publishers (CurrentUser)" -ForegroundColor Cyan

Write-Host "=== 3. Signing the Published Executable ===" -ForegroundColor Green
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
$exePath = "bin\Release\net10.0-windows\win-x64\publish\ThodMixer.exe"

# Sign the main executable
& $signtool sign /f $pfxPath /p $passwordText /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com $exePath
Write-Host "Successfully signed ThodMixer.exe" -ForegroundColor Cyan

Write-Host "=== 4. Creating Portable ZIP Archive ===" -ForegroundColor Green
$zipPath = "ThodMixer_Portable.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path $exePath -DestinationPath $zipPath -Force
Write-Host "Successfully created portable ZIP: $zipPath" -ForegroundColor Cyan

Write-Host "=== 5. Compiling Inno Setup Installer ===" -ForegroundColor Green
$iscc = "C:\Users\Thod\AppData\Local\Programs\Antigravity\resources\app\node_modules\innosetup\bin\ISCC.exe"
& $iscc setup.iss
Write-Host "Successfully compiled setup installer" -ForegroundColor Cyan

Write-Host "=== 6. Signing the Installer ===" -ForegroundColor Green
$setupPath = "ThodMixer_Setup.exe"
& $signtool sign /f $pfxPath /p $passwordText /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com $setupPath
Write-Host "Successfully signed ThodMixer_Setup.exe" -ForegroundColor Cyan

Write-Host "=== PROCESS COMPLETED SUCCESSFULLY ===" -ForegroundColor Green
Write-Host "Generated Files:" -ForegroundColor Yellow
Write-Host "1. Portable Version: d:\VisualProject\ThodMixer-master\ThodMixer_Portable.zip" -ForegroundColor White
Write-Host "2. Installer: d:\VisualProject\ThodMixer-master\ThodMixer_Setup.exe" -ForegroundColor White
Write-Host "3. Certificate: d:\VisualProject\ThodMixer-master\ThodMixer_Cert.pfx" -ForegroundColor White
