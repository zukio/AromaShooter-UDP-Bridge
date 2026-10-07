# Run with Windows PowerShell 5.1 (powershell.exe), not PowerShell 7.
# Close the bridge and official app first: the COM port is exclusive.
param(
    [ValidateSet('Simple', 'Intensity', 'Replace', 'ReplaceGap')]
    [string]$Mode = 'Intensity',
    [string]$Port = 'COM3'
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Use Windows PowerShell 5.1: powershell.exe -File tools/Test-UsbSdk.ps1' }
$sdkPath = Join-Path $PSScriptRoot '../lib/net472/AromaShooterWindowsSDK.dll'
# Windows PowerShell/.NET Framework rejects LoadFrom on some mapped drives.
# Use a local diagnostic copy, without changing the application's SDK file.
$localSdkFolder = Join-Path ([IO.Path]::GetTempPath()) ('AromaSdkDiagnostic-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $localSdkFolder)
$localSdkPath = Join-Path $localSdkFolder 'AromaShooterWindowsSDK.dll'
Copy-Item -LiteralPath (Resolve-Path $sdkPath).Path -Destination $localSdkPath
# The supplied SDK ZIP marks this DLL as downloaded. Trust only this diagnostic copy.
Unblock-File -LiteralPath $localSdkPath
# A standalone process also gives the SDK a writable executable directory for its log.
$source = @"
using System;
using System.Collections.Generic;
using System.Threading;
using AromaShooterWindowsSDK;
class UsbDiagnostic {
 static int Main(string[] args) {
  var usb = AromaShooterControllerUSB.SharedInstance;
  try {
   Console.WriteLine("Connecting to " + args[1]);
   if (!usb.Connect(args[1])) { Console.WriteLine("CONNECT FAILED"); return 2; }
   var devices = usb.GetConnectedDevices();
   if (devices.Count != 1) { Console.WriteLine("Expected one device, found " + devices.Count); return 3; }
   string serial = devices[0];
   Console.WriteLine("Device=" + serial + " Mode=" + args[0] + " Chamber=1 Duration=3000ms");
   if (args[0] == "Replace" || args[0] == "ReplaceGap") {
    usb.StopWithIntensity(serial, new[]{1,2,3,4,5,6}, true, true);
    if (args[0] == "ReplaceGap") Thread.Sleep(100);
   }
   if (args[0] == "Simple") usb.ShootSimple(3000, new[]{1}, true, serial);
   else usb.ShootWithIntensity(3000, new List<AromaChamber>{new AromaChamber{number=1, concentration=100}},100,0,serial);
   Console.WriteLine("SDK returned; observe fan/airflow.");
   Thread.Sleep(4000);
   return 0;
  } catch(Exception ex) { Console.WriteLine(ex); return 1; }
  finally {
   try {usb.StopAllSimple();} catch(Exception ex) {Console.WriteLine(ex.Message);}
   try {usb.StopAllWithIntensity(new[]{1,2,3,4,5,6},true,true);} catch(Exception ex) {Console.WriteLine(ex.Message);}
   try {usb.DisconnectAll();} catch(Exception ex) {Console.WriteLine(ex.Message);}
  }
 }
}
"@
$sourcePath = Join-Path $localSdkFolder 'UsbDiagnostic.cs'
$exePath = Join-Path $localSdkFolder 'UsbDiagnostic.exe'
Set-Content -LiteralPath $sourcePath -Value $source -Encoding UTF8
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /target:exe "/out:$exePath" "/reference:$localSdkPath" $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic compilation failed' }
& $exePath $Mode $Port
exit $LASTEXITCODE
