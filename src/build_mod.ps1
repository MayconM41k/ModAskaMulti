$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outDll = 'F:\Steam\steamapps\common\ASKA\BepInEx\plugins\AskaMultipleEye\AskaMultipleEye.dll'
$src = 'C:\Users\M41K\.gemini\antigravity\brain\821e8e65-fa14-4c39-b7a9-8ed48987209b\scratch\AskaMultipleEye.cs'
$cleanDir = 'C:\Users\M41K\.gemini\antigravity\brain\821e8e65-fa14-4c39-b7a9-8ed48987209b\scratch\interop-clean'
$dotnetDir = 'F:\Steam\steamapps\common\ASKA\dotnet'
$coreDir = 'F:\Steam\steamapps\common\ASKA\BepInEx\core'

$refs = @(
    "$dotnetDir\System.Private.CoreLib.dll",
    "$dotnetDir\netstandard.dll",
    "$dotnetDir\mscorlib.dll",
    "$dotnetDir\System.Runtime.dll",
    "$dotnetDir\System.Collections.dll",
    "$dotnetDir\System.Reflection.dll",
    "$coreDir\0Harmony.dll",
    "$coreDir\BepInEx.Core.dll",
    "$coreDir\BepInEx.Unity.IL2CPP.dll",
    "$coreDir\Il2CppInterop.Runtime.dll"
)
Get-ChildItem "$cleanDir\*.dll" | ForEach-Object { $refs += $_.FullName }

$refArgs = $refs | ForEach-Object { "/r:`"$($_)`"" }
& $csc /nologo /nostdlib /noconfig /target:library /optimize+ /out:"$outDll" $refArgs "$src"
if ($LASTEXITCODE -ne 0) {
    throw "csc failed with exit code $LASTEXITCODE"
}

Add-Type -Path "$coreDir\Mono.Cecil.dll"
$readerParams = New-Object Mono.Cecil.ReaderParameters
$readerParams.InMemory = $true
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($outDll, $readerParams)
$mainMod = $asm.MainModule
$canonicalRef = $mainMod.AssemblyReferences | Where-Object { $_.Name -eq 'Il2CppInterop.Runtime' -and $_.Version.Major -ge 1 } | Select-Object -First 1
$zeroRefs = @($mainMod.AssemblyReferences | Where-Object { $_.Name -eq 'Il2CppInterop.Runtime' -and $_.Version.Major -eq 0 })
if ($canonicalRef -and $zeroRefs.Count -gt 0) {
    foreach ($tr in $mainMod.GetTypeReferences()) {
        if ($tr.Scope -is [Mono.Cecil.AssemblyNameReference] -and $tr.Scope.Name -eq 'Il2CppInterop.Runtime' -and $tr.Scope.Version.Major -eq 0) {
            $tr.Scope = $canonicalRef
        }
    }
    foreach ($zr in $zeroRefs) {
        [void]$mainMod.AssemblyReferences.Remove($zr)
    }
    $asm.Write($outDll)
}
$asm.Dispose()

$cacheFile = 'F:\Steam\steamapps\common\ASKA\BepInEx\cache\chainloader_typeloader.dat'
if (Test-Path $cacheFile) {
    Remove-Item $cacheFile -Force
}

$usbDll = 'G:\Mod_ASKA_MultiAldeia\BepInEx\plugins\AskaMultipleEye\AskaMultipleEye.dll'
try {
    if (Test-Path 'G:\Mod_ASKA_MultiAldeia\BepInEx\plugins\AskaMultipleEye' -ErrorAction SilentlyContinue) {
        Copy-Item -Path $outDll -Destination $usbDll -Force -ErrorAction SilentlyContinue
        Copy-Item -Path 'F:\Steam\steamapps\common\ASKA\BepInEx\config\AskaMultipleEye.cfg' -Destination 'G:\Mod_ASKA_MultiAldeia\BepInEx\config\AskaMultipleEye.cfg' -Force -ErrorAction SilentlyContinue
        if (Test-Path 'G:\Mod_ASKA_MultiAldeia\BepInEx\cache\chainloader_typeloader.dat' -ErrorAction SilentlyContinue) {
            Remove-Item 'G:\Mod_ASKA_MultiAldeia\BepInEx\cache\chainloader_typeloader.dat' -Force -ErrorAction SilentlyContinue
        }
        Write-Output "Synced to USB: $usbDll"
    }
} catch {
    Write-Output "USB copy skipped (drive not accessible in current sandbox)"
}

Write-Output "Build succeeded: $outDll ($((Get-Item $outDll).Length) bytes)"
