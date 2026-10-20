# A Bloom search provider: lists files whose name holds the words, from the folder in $env:BLOOM_FIND_ROOT
# (default: Documents). Bloom starts it with the words as the last argument and reads one JSON object from stdout.
param([Parameter(Position = 0)][string]$Words = '')

[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$root = if ($env:BLOOM_FIND_ROOT) { $env:BLOOM_FIND_ROOT } else { [Environment]::GetFolderPath('MyDocuments') }
$pattern = '*' + [Management.Automation.WildcardPattern]::Escape($Words) + '*'

$items = @(
    Get-ChildItem -LiteralPath $root -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 20 |
        ForEach-Object {
            @{
                title    = $_.Name
                subtitle = $_.DirectoryName
                action   = @{ type = 'run'; value = @('explorer.exe', "/select,$($_.FullName)") }
            }
        }
)
@{ items = $items } | ConvertTo-Json -Depth 6 -Compress
