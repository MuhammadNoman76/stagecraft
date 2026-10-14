# Generates the sample decks' video and music and the tiny test media with ffmpeg (no third-party media):
#   powershell -ExecutionPolicy Bypass -File tools\make-sample-media.ps1
# Writes:
#   src\Stagecraft.Core\Assets\SampleMedia\aurora-loop.mp4   8 s 1280 x 720 H.264 / AAC: drifting colour bands and a soft chord
#   src\Stagecraft.Core\Assets\SampleMedia\soft-pad.m4a      16 s AAC: a slow synthesized G major pad (sine tones)
#   src\Stagecraft.Core\Assets\SampleMedia\aurora-poster.jpg 960 x 540 JPEG: the video's frame at 3 s (its poster frame)
#   tests\Stagecraft.Tests\Media\test-clip.mp4               2 s 320 x 180 H.264 / AAC: ffmpeg's test pattern and a 440 Hz tone
#   tests\Stagecraft.Tests\Media\test-tone.mp3 / .wav         short sine tones
# Everything is made from ffmpeg's built-in generators (gradients, testsrc2, sine, aevalsrc), so it carries no copyright.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg) { throw "ffmpeg isn't on PATH" }
$samples = Join-Path $root "src\Stagecraft.Core\Assets\SampleMedia"
$tests = Join-Path $root "tests\Stagecraft.Tests\Media"
New-Item -ItemType Directory -Force $samples, $tests | Out-Null

function Run([string[]]$arguments) {
    & $ffmpeg -hide_banner -loglevel error -y @arguments
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed: $arguments" }
}

$chord = "0.10*sin(2*PI*220*t)*(0.65+0.35*sin(2*PI*0.25*t))+0.08*sin(2*PI*277.18*t)*(0.6+0.4*sin(2*PI*0.2*t+1))+0.07*sin(2*PI*329.63*t)+0.04*sin(2*PI*440*t)*(0.5+0.5*sin(2*PI*0.125*t))"
Run @('-f', 'lavfi', '-i', 'gradients=s=1280x720:r=30:d=8:c0=0x14142B:c1=0x6C5CE7:c2=0x00B8D9:c3=0x14142B:c4=0xFF6B9A:n=5:speed=0.0045:type=linear:seed=11',
      '-f', 'lavfi', '-i', "aevalsrc=${chord}:s=48000:d=8",
      '-vf', 'gblur=sigma=30,noise=alls=3:allf=t,format=yuv420p', '-af', 'afade=t=in:d=1.5,afade=t=out:st=6.5:d=1.5,lowpass=f=2400',
      '-c:v', 'libx264', '-profile:v', 'main', '-preset', 'slow', '-crf', '27', '-movflags', '+faststart', '-c:a', 'aac', '-b:a', '96k',
      (Join-Path $samples 'aurora-loop.mp4'))
Run @('-ss', '3', '-i', (Join-Path $samples 'aurora-loop.mp4'), '-frames:v', '1', '-vf', 'scale=960:-2', '-q:v', '4', (Join-Path $samples 'aurora-poster.jpg'))

$pad = "0.11*sin(2*PI*196*t)*(0.7+0.3*sin(2*PI*0.2*t))+0.08*sin(2*PI*246.94*t)*(0.6+0.4*sin(2*PI*0.15*t+2))+0.07*sin(2*PI*293.66*t)+0.05*sin(2*PI*392*t)*(0.5+0.5*sin(2*PI*0.1*t))+0.03*sin(2*PI*587.33*t)*(0.5+0.5*sin(2*PI*0.33*t))"
Run @('-f', 'lavfi', '-i', "aevalsrc=${pad}:s=44100:d=16", '-af', 'afade=t=in:d=2,afade=t=out:st=13:d=3,lowpass=f=2000,aecho=0.8:0.6:120:0.25',
      '-c:a', 'aac', '-b:a', '80k', (Join-Path $samples 'soft-pad.m4a'))

Run @('-f', 'lavfi', '-i', 'testsrc2=s=320x180:r=15:d=2', '-f', 'lavfi', '-i', 'sine=f=440:d=2', '-vf', 'format=yuv420p',
      '-c:v', 'libx264', '-profile:v', 'baseline', '-crf', '32', '-c:a', 'aac', '-b:a', '48k', '-movflags', '+faststart', (Join-Path $tests 'test-clip.mp4'))
Run @('-f', 'lavfi', '-i', 'sine=f=523.25:d=1', '-c:a', 'libmp3lame', '-b:a', '64k', (Join-Path $tests 'test-tone.mp3'))
Run @('-f', 'lavfi', '-i', 'sine=f=330:d=0.5', '-ac', '1', '-ar', '22050', (Join-Path $tests 'test-tone.wav'))
Write-Host "Done: $samples, $tests" -ForegroundColor Green
