param([string]$OutputDir)
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.SelectVoice('Microsoft Zira Desktop')
$synth.Rate = 1
$synth.Volume = 100
$scenes = Get-Content -Raw (Join-Path $PSScriptRoot 'timeline.json') | ConvertFrom-Json
for ($i = 0; $i -lt $scenes.Count; $i++) {
    $synth.SetOutputToWaveFile((Join-Path $OutputDir ('voice-{0}.wav' -f $i)))
    $synth.Speak($scenes[$i].voice)
    $synth.SetOutputToNull()
}
$synth.Dispose()
