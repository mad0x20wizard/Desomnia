param([Parameter(Mandatory = $true)][string]$Path)
$ErrorActionPreference = 'Stop'
$data = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
function Median($values) {
    $sorted = @($values | Sort-Object)
    return ($sorted[[int][Math]::Floor(($sorted.Count - 1) / 2)] + $sorted[[int][Math]::Floor($sorted.Count / 2)]) / 2
}
foreach ($value in $data.Results | Where-Object Variant -eq 'Struct') {
    $reference = $data.Results | Where-Object { $_.IP -eq $value.IP -and $_.FrameBytes -eq $value.FrameBytes -and $_.Work -eq $value.Work -and $_.Variant -eq 'Class' }
    $valueNs = Median $value.Samples.NanosecondsPerPacket
    $referenceNs = Median $reference.Samples.NanosecondsPerPacket
    $valueBytes = Median $value.Samples.BytesPerPacket
    $referenceBytes = Median $reference.Samples.BytesPerPacket
    $pairs = foreach ($sample in $value.Samples) {
        $other = $reference.Samples | Where-Object Round -eq $sample.Round
        ($other.NanosecondsPerPacket / $sample.NanosecondsPerPacket - 1) * 100
    }
    $valuePause = ($value.Samples | Measure-Object GcPauseMilliseconds -Sum).Sum
    $referencePause = ($reference.Samples | Measure-Object GcPauseMilliseconds -Sum).Sum
    $valueSeconds = ($value.Samples | Measure-Object Seconds -Sum).Sum
    $referenceSeconds = ($reference.Samples | Measure-Object Seconds -Sum).Sum
    [pscustomobject]@{
        IP = $value.IP
        Frame = $value.FrameBytes
        Work = $value.Work
        StructNs = [Math]::Round($valueNs, 1)
        ClassNs = [Math]::Round($referenceNs, 1)
        ClassTimeDeltaPct = [Math]::Round(($referenceNs / $valueNs - 1) * 100, 2)
        PairedMedianDeltaPct = [Math]::Round((Median $pairs), 2)
        PairedMinDeltaPct = [Math]::Round(($pairs | Measure-Object -Minimum).Minimum, 2)
        PairedMaxDeltaPct = [Math]::Round(($pairs | Measure-Object -Maximum).Maximum, 2)
        StructBytes = $valueBytes
        ClassBytes = $referenceBytes
        StructGBps = [Math]::Round($value.FrameBytes / $valueNs, 2)
        ClassGBps = [Math]::Round($value.FrameBytes / $referenceNs, 2)
        StructGcPausePct = [Math]::Round($valuePause / $valueSeconds / 10, 2)
        ClassGcPausePct = [Math]::Round($referencePause / $referenceSeconds / 10, 2)
        SavedMBpsAt1GBps = [Math]::Round(($referenceBytes - $valueBytes) * 1000 / $value.FrameBytes, 1)
        SavedMBpsAt2_5GBps = [Math]::Round(($referenceBytes - $valueBytes) * 2500 / $value.FrameBytes, 1)
    }
}
