# Downloads the open-licensed (SIL OFL 1.1) fonts Stagecraft bundles, checks them and copies them to
# src\Stagecraft.Core\Fonts (with each family's OFL.txt and a SOURCE.txt listing URLs and SHA-256 hashes).
#   powershell -ExecutionPolicy Bypass -File tools\get-fonts.ps1
#   powershell -ExecutionPolicy Bypass -File tools\get-fonts.ps1 -Scratch D:\tmp\fonts -NoInstall   # download + verify only
#
# Sources (both official):
#   - the static TrueType instances Google Fonts serves (fonts.googleapis.com/css2 -> fonts.gstatic.com)
#   - each family's licence file from the google/fonts repository on GitHub
# The fonts are loaded privately by Stagecraft (WPF FontFamily with a file URI); they are never installed.
param(
    [string]$Scratch = (Join-Path ([IO.Path]::GetTempPath()) ("stagecraft-fonts-" + [guid]::NewGuid().ToString("N").Substring(0, 8))),
    [switch]$NoInstall
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root "src\Stagecraft.Core\Fonts"
Add-Type -AssemblyName PresentationCore, WindowsBase
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# Family, Google Fonts query, google/fonts licence folder, file prefix
$families = @(
    @{ Name = "Inter";             Query = "Inter:ital,wght@0,400;0,500;0,600;0,700;0,800;1,400";             Ofl = "inter";            Prefix = "Inter" },
    @{ Name = "Plus Jakarta Sans"; Query = "Plus+Jakarta+Sans:ital,wght@0,400;0,500;0,600;0,700;0,800;1,400"; Ofl = "plusjakartasans";  Prefix = "PlusJakartaSans" },
    @{ Name = "DM Sans";           Query = "DM+Sans:ital,wght@0,400;0,500;0,700;1,400";                       Ofl = "dmsans";           Prefix = "DMSans" },
    @{ Name = "Space Grotesk";     Query = "Space+Grotesk:wght@400;500;700";                                  Ofl = "spacegrotesk";     Prefix = "SpaceGrotesk" },
    @{ Name = "Playfair Display";  Query = "Playfair+Display:ital,wght@0,400;0,700;1,400;1,700";              Ofl = "playfairdisplay";  Prefix = "PlayfairDisplay" },
    @{ Name = "DM Serif Display";  Query = "DM+Serif+Display:ital@0;1";                                       Ofl = "dmserifdisplay";   Prefix = "DMSerifDisplay" },
    @{ Name = "Source Serif 4";    Query = "Source+Serif+4:ital,wght@0,400;0,600;0,700;1,400";                Ofl = "sourceserif4";     Prefix = "SourceSerif4" },
    @{ Name = "JetBrains Mono";    Query = "JetBrains+Mono:wght@400;500;700";                                 Ofl = "jetbrainsmono";    Prefix = "JetBrainsMono" }
)

$weightNames = @{ 400 = "Regular"; 500 = "Medium"; 600 = "SemiBold"; 700 = "Bold"; 800 = "ExtraBold" }

New-Item -ItemType Directory -Force $Scratch | Out-Null
if ((Get-ChildItem $Scratch -Force | Measure-Object).Count -gt 0) { throw "The scratch folder must be empty: $Scratch" }
$verify = Join-Path ([IO.Path]::GetTempPath()) ("stagecraft-fontcheck-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
Write-Host "Downloading into $Scratch"

$client = New-Object Net.WebClient
# An old user agent makes Google Fonts answer with TrueType files (static instances) instead of WOFF2.
$userAgent = "Wget/1.21"
$sources = New-Object Collections.Generic.List[string]
$sources.Add("Stagecraft bundled fonts - downloaded by tools\get-fonts.ps1 on " + (Get-Date -Format "yyyy-MM-dd"))
$sources.Add("All fonts: SIL Open Font License 1.1 (see OFL-*.txt). Files are unmodified.")
$sources.Add("")

foreach ($f in $families) {
    $client.Headers["User-Agent"] = $userAgent
    $css = $client.DownloadString("https://fonts.googleapis.com/css2?family=" + $f.Query)
    $faces = [regex]::Matches($css, "@font-face\s*\{([^}]*)\}")
    if ($faces.Count -eq 0) { throw "No font faces listed for $($f.Name)" }
    $sources.Add("$($f.Name)")
    foreach ($face in $faces) {
        $block = $face.Groups[1].Value
        $style = [regex]::Match($block, "font-style:\s*(\w+)").Groups[1].Value
        $weight = [int][regex]::Match($block, "font-weight:\s*(\d+)").Groups[1].Value
        $src = [regex]::Match($block, "src:\s*url\(([^)]+)\)\s*format\('truetype'\)")
        if (-not $src.Success) { throw "Google Fonts didn't offer a TrueType file for $($f.Name) $weight $style" }
        $url = $src.Groups[1].Value
        $suffix = $weightNames[$weight]
        if ($style -eq "italic") { $suffix = if ($weight -eq 400) { "Italic" } else { $suffix + "Italic" } }
        $file = Join-Path $Scratch ("{0}-{1}.ttf" -f $f.Prefix, $suffix)
        $client.Headers["User-Agent"] = $userAgent
        $client.DownloadFile($url, $file)

        # Verify: a real TrueType font of the expected family, weight and style, with Latin coverage.
        # (Each file is opened from a folder of its own: WPF's font cache trips over a folder whose files change.)
        $probeDir = Join-Path $verify ([guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Force $probeDir | Out-Null
        $probe = Join-Path $probeDir (Split-Path $file -Leaf)
        Copy-Item $file $probe
        $gt = New-Object Windows.Media.GlyphTypeface (New-Object Uri $probe)
        $family = $gt.FamilyNames[[Globalization.CultureInfo]::GetCultureInfo("en-US")]
        if ($family -ne $f.Name) { throw "$file reports family '$family', expected '$($f.Name)'" }
        if ($gt.Weight.ToOpenTypeWeight() -ne $weight) { throw "$file has weight $($gt.Weight.ToOpenTypeWeight()), expected $weight" }
        $isItalic = $gt.Style -ne [Windows.FontStyles]::Normal
        if ($isItalic -ne ($style -eq "italic")) { throw "$file has the wrong style ($($gt.Style))" }
        foreach ($code in 0x41, 0x5A, 0x61, 0x7A, 0x30, 0x39, 0x26, 0xE9, 0x20AC, 0x2014) {
            if (-not $gt.CharacterToGlyphMap.ContainsKey($code)) { throw ("{0} has no glyph for U+{1:X4}" -f $file, $code) }
        }
        $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
        $sources.Add(("  {0}  {1} glyphs  sha256 {2}" -f (Split-Path $file -Leaf), $gt.GlyphCount, $hash))
        $sources.Add("    $url")
        Write-Host ("  {0,-34} {1,6:N0} KB  {2} glyphs" -f (Split-Path $file -Leaf), ((Get-Item $file).Length / 1KB), $gt.GlyphCount)
    }

    $licenseUrl = "https://raw.githubusercontent.com/google/fonts/main/ofl/$($f.Ofl)/OFL.txt"
    $licenseFile = Join-Path $Scratch ("OFL-{0}.txt" -f $f.Prefix)
    $client.Headers["User-Agent"] = $userAgent
    $client.DownloadFile($licenseUrl, $licenseFile)
    $licenseText = [IO.File]::ReadAllText($licenseFile)
    if ($licenseText -notmatch "SIL OPEN FONT LICENSE") { throw "$licenseFile doesn't look like the SIL Open Font License" }
    $sources.Add("  licence: $licenseUrl")
    $sources.Add("")
}

[IO.File]::WriteAllLines((Join-Path $Scratch "SOURCE.txt"), $sources, (New-Object Text.UTF8Encoding($false)))
Write-Host "Verified $((Get-ChildItem $Scratch -Filter *.ttf).Count) font files."
# The probe copies stay locked by WPF until this process ends; remove what we can.
Remove-Item $verify -Recurse -Force -ErrorAction SilentlyContinue

if (-not $NoInstall) {
    New-Item -ItemType Directory -Force $target | Out-Null
    Get-ChildItem $target -File | Where-Object { $_.Extension -in ".ttf", ".txt" } | Remove-Item -Force
    Copy-Item (Join-Path $Scratch "*") $target
    Write-Host "Copied to $target"
}
