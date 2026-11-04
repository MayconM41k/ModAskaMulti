Add-Type -Path 'F:\Steam\steamapps\common\ASKA\BepInEx\core\Mono.Cecil.dll'

$cleanAsm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('F:\Steam\steamapps\common\ASKA\BepInEx\interop\Assembly-CSharp.dll')
$modAsm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('F:\Steam\steamapps\common\ASKA\BepInEx\plugins\AskaMultipleEye\AskaMultipleEye.dll')

Write-Output "=== 1. VERIFICANDO REFERENCIAS DA DLL DO MOD ==="
foreach ($ar in $modAsm.MainModule.AssemblyReferences) {
    Write-Output ("  Ref: {0} v{1}" -f $ar.Name, $ar.Version)
}

Write-Output "`n=== 2. VERIFICANDO GANCHOS HARMONY DO MOD ==="
$patchedTargets = @()
foreach ($t in $modAsm.MainModule.Types) {
    foreach ($ca in $t.CustomAttributes) {
        if ($ca.AttributeType.Name -match 'HarmonyPatch') {
            $declaringType = $null
            $methodName = $null
            if ($ca.ConstructorArguments.Count -ge 2) {
                $declaringType = $ca.ConstructorArguments[0].Value.FullName
                $methodName = $ca.ConstructorArguments[1].Value
            } elseif ($ca.ConstructorArguments.Count -eq 1) {
                if ($ca.ConstructorArguments[0].Value -is [Mono.Cecil.TypeReference]) {
                    $declaringType = $ca.ConstructorArguments[0].Value.FullName
                } else {
                    $methodName = $ca.ConstructorArguments[0].Value
                }
            }
            $patchedTargets += [PSCustomObject]@{
                PatchClass = $t.Name
                TargetType = $declaringType
                Method = $methodName
            }
        }
    }
}

Write-Output ("Total de classes de Patch Harmony: " + $patchedTargets.Count)

Write-Output "`n=== 3. VERIFICANDO SEGURANCA ICF DE CADA METODO NO GAMEASSEMBLY ==="
$hasIcfCrashRisk = $false
foreach ($pt in $patchedTargets) {
    if (-not $pt.TargetType -or -not $pt.Method) { 
        Write-Output ("  [AVISO] Patch sem anotacao direta de tipo/metodo: " + $pt.PatchClass)
        continue 
    }
    
    $targetTypeDef = $cleanAsm.MainModule.GetType($pt.TargetType)
    if (-not $targetTypeDef) {
        Write-Output ("  [ERRO CRITICO] Tipo alvo nao encontrado: " + $pt.TargetType)
        $hasIcfCrashRisk = $true
        continue
    }
    
    $methods = @($targetTypeDef.Methods | Where-Object { $_.Name -eq $pt.Method })
    if ($methods.Count -eq 0) {
        Write-Output ("  [ERRO CRITICO] Metodo alvo nao encontrado: {0}::{1}" -f $pt.TargetType, $pt.Method)
        $hasIcfCrashRisk = $true
        continue
    }
    
    foreach ($m in $methods) {
        $ca = $m.CustomAttributes | Where-Object { $_.AttributeType.Name -match 'CachedScanResults' }
        if ($ca) {
            $rs = ($ca.Fields | ? Name -eq RefRangeStart).Argument.Value
            $re = ($ca.Fields | ? Name -eq RefRangeEnd).Argument.Value
            $xs = ($ca.Fields | ? Name -eq XrefRangeStart).Argument.Value
            $xe = ($ca.Fields | ? Name -eq XrefRangeEnd).Argument.Value
            $isFolded = ($rs -eq $xs -and $re -eq $xe) -or ($xs -eq 0 -and $xe -eq 0)
            if ($isFolded) {
                Write-Output ("  [PERIGO ICF] {0}::{1} tem corpo unificado (Ref={2}..{3}, Xref={4}..{5})!" -f $pt.TargetType, $m.Name, $rs, $re, $xs, $xe)
                $hasIcfCrashRisk = $true
            } else {
                Write-Output ("  [SEGURO] {0}::{1} -> Xref={2}..{3} (distinto, sem colisoes)" -f $pt.TargetType, $m.Name, $xs, $xe)
            }
        } else {
            Write-Output ("  [INFO] {0}::{1} -> Sem CachedScanResults (metodo customizado/managed)" -f $pt.TargetType, $m.Name)
        }
    }
}

Write-Output "`n=== 4. VERIFICANDO ARQUIVOS ORIGINAIS DO JOGO ==="
$origFiles = @(
    'F:\Steam\steamapps\common\ASKA\Aska.exe',
    'F:\Steam\steamapps\common\ASKA\GameAssembly.dll',
    'F:\Steam\steamapps\common\ASKA\UnityPlayer.dll',
    'F:\Steam\steamapps\common\ASKA\baselib.dll'
)
foreach ($f in $origFiles) {
    if (Test-Path $f) {
        $item = Get-Item $f
        Write-Output ("  Arquivo do jogo integro: {0} ({1} bytes, Modificado: {2})" -f $item.Name, $item.Length, $item.LastWriteTime)
    } else {
        Write-Output ("  [ERRO] Arquivo ausente: " + $f)
    }
}

Write-Output "`n=== RESULTADO FINAL DA AUDITORIA ==="
if (-not $hasIcfCrashRisk) {
    Write-Output "STATUS: 100% SEGURO. Nenhum risco de crash ou instabilidade detectado!"
} else {
    Write-Output "STATUS: ATENCAO DETECTADA!"
}
