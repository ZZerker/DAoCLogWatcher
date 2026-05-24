# Extracts DoT and AoE nuke spell data from the Eden charplanner JSON into a compact spells.json.
# Re-run this script whenever Eden updates their spell data (happens every few months).
# Usage: .\extract-dots.ps1  (from the repo root)

$charplanPath = Join-Path $PSScriptRoot "data\eden\charplan.json"
$outputPath   = Join-Path $PSScriptRoot "DAoCLogWatcher.Core\Resources\spells.json"

function ConvertTo-Seconds([string]$value) {
    if (-not $value -or $value -eq "Unlimited") { return -1 }
    if ($value -match "^(\d+)s$")              { return [int]$Matches[1] }
    if ($value -match "^(\d+):(\d+) min$")     { return [int]$Matches[1] * 60 + [int]$Matches[2] }
    if ($value -match "^(\d+):(\d+) h$")       { return [int]$Matches[1] * 3600 + [int]$Matches[2] * 60 }
    return -1
}

Write-Host "Reading $charplanPath ..."
$json = [System.IO.File]::ReadAllText($charplanPath, [System.Text.Encoding]::UTF8)
$data = $json | ConvertFrom-Json

$seen        = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$dotList     = [System.Collections.Generic.List[hashtable]]::new()
$aoenukeList = [System.Collections.Generic.List[hashtable]]::new()

foreach ($class in $data) {
    foreach ($spec in $class.specs) {
        if (-not $spec.spellLines) { continue }
        foreach ($spellLine in $spec.spellLines) {
            if (-not $spellLine.spellGroups) { continue }
            foreach ($spellGroup in $spellLine.spellGroups) {
                if (-not $spellGroup.skills) { continue }
                foreach ($skill in $spellGroup.skills) {
                    if (-not $skill.attributes) { continue }

                    $attrs = @{}
                    foreach ($attr in $skill.attributes) {
                        if ($attr -and $attr.Count -ge 2) {
                            $attrs[$attr[0]] = $attr[1]
                        }
                    }

                    if ($seen.Contains($skill.name)) { continue }

                    if ($attrs["Type"] -eq "Damage Over Time") {
                        [void]$seen.Add($skill.name)
                        $dotList.Add(@{
                            name             = $skill.name
                            durationSeconds  = ConvertTo-Seconds $attrs["Duration"]
                            frequencySeconds = ConvertTo-Seconds ($attrs["Frequency"])
                            isAoe            = $attrs.ContainsKey("Radius")
                            isAoeNuke        = $false
                        })
                    }
                    # Instant AoE nukes: Direct Damage + Radius present + no Frequency.
                    # Also includes Bolt + Radius (Bainshee cascade line, Explosive Orbs, etc.) —
                    # same log format as DD AoE. Excludes rain/pulse DDs (Direct Damage + Radius + Frequency)
                    # and turret PBAoE spells (Turret PBAoE type, different log format).
                    elseif (($attrs["Type"] -eq "Direct Damage" -or $attrs["Type"] -eq "Bolt") -and $attrs.ContainsKey("Radius") -and -not $attrs.ContainsKey("Frequency")) {
                        [void]$seen.Add($skill.name)
                        $aoenukeList.Add(@{
                            name             = $skill.name
                            durationSeconds  = 0
                            frequencySeconds = 0
                            isAoe            = $false
                            isAoeNuke        = $true
                        })
                    }
                }
            }
        }
    }
}

$combined  = [System.Collections.Generic.List[hashtable]]::new()
$combined.AddRange($dotList)
$combined.AddRange($aoenukeList)

$sorted    = @($combined | Sort-Object { $_["name"] })
$outputJson = ConvertTo-Json -InputObject $sorted -Compress -Depth 3

$outputDir = Split-Path $outputPath -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

[System.IO.File]::WriteAllText($outputPath, $outputJson, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Wrote $($dotList.Count) DoT spells and $($aoenukeList.Count) AoE nuke spells to $outputPath"
