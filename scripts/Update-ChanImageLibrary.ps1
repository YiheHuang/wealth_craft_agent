param(
    [string]$OutputRoot = "docs/chan_images",
    [int]$OriginalStart = 1,
    [int]$OriginalEnd = 108,
    [switch]$SkipDownload
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function New-WebClient {
    $client = [System.Net.WebClient]::new()
    $client.Encoding = [System.Text.Encoding]::UTF8
    $client.Headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
    $client.Headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8"
    return $client
}

function Get-PageHtml([string]$Url) {
    $client = New-WebClient
    try {
        return $client.DownloadString($Url)
    }
    catch {
        Write-Warning "Failed to fetch page: $Url - $($_.Exception.Message)"
        return $null
    }
    finally {
        $client.Dispose()
    }
}

function Save-RemoteFile([string]$Url, [string]$Path) {
    $client = New-WebClient
    try {
        $dir = Split-Path -Parent $Path
        if (-not (Test-Path $dir)) {
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
        }
        $client.DownloadFile($Url, $Path)
        return $true
    }
    catch {
        Write-Warning "Failed to download image: $Url - $($_.Exception.Message)"
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Decode-Html([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }
    return [System.Net.WebUtility]::HtmlDecode($Text)
}

function ConvertTo-PlainText([string]$Html) {
    if ([string]::IsNullOrWhiteSpace($Html)) {
        return ""
    }
    $text = [regex]::Replace($Html, "<script[\s\S]*?</script>", " ", "IgnoreCase")
    $text = [regex]::Replace($text, "<style[\s\S]*?</style>", " ", "IgnoreCase")
    $text = [regex]::Replace($text, "<[^>]+>", " ")
    $text = Decode-Html $text
    $text = [regex]::Replace($text, "\s+", " ").Trim()
    return $text
}

function Resolve-Url([string]$BaseUrl, [string]$Href) {
    $href = Decode-Html $Href
    if ([string]::IsNullOrWhiteSpace($href)) {
        return ""
    }
    if ($href.StartsWith("//")) {
        return "https:$href"
    }
    if ($href -match "^https?://") {
        return $href
    }
    return ([Uri]::new([Uri]$BaseUrl, $href)).AbsoluteUri
}

function Get-SafeFileName([string]$Name) {
    $decoded = Decode-Html $Name
    $decoded = [Uri]::UnescapeDataString($decoded)
    $invalid = [IO.Path]::GetInvalidFileNameChars()
    foreach ($ch in $invalid) {
        $decoded = $decoded.Replace($ch, "_")
    }
    $decoded = [regex]::Replace($decoded, "\s+", "_").Trim("_")
    if ([string]::IsNullOrWhiteSpace($decoded)) {
        return "image.png"
    }
    return $decoded
}

function Get-PageTitle([string]$Html) {
    $h2 = [regex]::Match($Html, "<h2[^>]*>([\s\S]*?)</h2>", "IgnoreCase")
    if ($h2.Success) {
        return ConvertTo-PlainText $h2.Groups[1].Value
    }
    $title = [regex]::Match($Html, "<title[^>]*>([\s\S]*?)</title>", "IgnoreCase")
    if ($title.Success) {
        return ConvertTo-PlainText $title.Groups[1].Value
    }
    return ""
}

function Get-PageDate([string]$Html) {
    $h5 = [regex]::Match($Html, "<h5[^>]*>([\s\S]*?)</h5>", "IgnoreCase")
    if ($h5.Success) {
        return ConvertTo-PlainText $h5.Groups[1].Value
    }
    $date = [regex]::Match($Html, "\d{4}-\d{2}-\d{2}(?:\s+\d{2}:\d{2})?")
    if ($date.Success) {
        return $date.Value
    }
    return ""
}

function Get-ContextAround([string]$Html, [int]$Index, [int]$Length) {
    $beforeStart = [Math]::Max(0, $Index - $Length)
    $beforeLen = $Index - $beforeStart
    $afterStart = [Math]::Min($Html.Length, $Index)
    $afterLen = [Math]::Min($Length, $Html.Length - $afterStart)
    $before = ConvertTo-PlainText $Html.Substring($beforeStart, $beforeLen)
    $after = ConvertTo-PlainText $Html.Substring($afterStart, $afterLen)
    return @{ Before = $before; After = $after }
}

function Get-Tags([string]$Title, [string]$Before, [string]$After, [string]$Collection, [string]$ArticleKey) {
    $tags = New-Object System.Collections.Generic.List[string]
    $tags.Add("chan-theory")
    if ($Collection -eq "original") {
        $tags.Add("original-text")
    }
    else {
        $tags.Add("illustrated-course")
    }
    $tags.Add($ArticleKey)

    $numberMatch = [regex]::Match($ArticleKey, "\d+")
    $articleNumber = if ($numberMatch.Success) { [int]$numberMatch.Value } else { 0 }

    if ($Collection -eq "illustrated" -or @(56,57,58,59,60,61,69,70,81,88,89,90) -contains $articleNumber) {
        $tags.Add("chart-example")
    }
    if (@(62,63,64,65,67,69,71,77,78,79,81,82,83,84) -contains $articleNumber) {
        $tags.Add("fractal")
        $tags.Add("bi")
        $tags.Add("segment")
    }
    if (@(20,21,24,25,27,36,37,38,39,40,43,44,53,56,57,58,59,60,61,68,70,86,91,92,93,99,102) -contains $articleNumber) {
        $tags.Add("zhongshu")
        $tags.Add("trend-structure")
    }
    if (@(24,25,27,37,43,44,56,57,58,59,60,61,70,89,90) -contains $articleNumber) {
        $tags.Add("divergence")
        $tags.Add("macd")
    }
    if (@(20,21,53,56,57,58,59,60,61,68,70,92) -contains $articleNumber) {
        $tags.Add("buy-sell-point")
    }
    if (@(32,33,36,38,39,40,68,86) -contains $articleNumber) {
        $tags.Add("decomposition")
        $tags.Add("multi-timeframe")
    }
    if (@(61) -contains $articleNumber) {
        $tags.Add("interval-nesting")
    }
    if ($ArticleKey -eq "t36") {
        $tags.Add("connection-composition")
    }
    if ($ArticleKey -eq "t38") {
        $tags.Add("same-level-decomposition")
    }
    if ("$Title $Before $After" -match "MACD|DIF|DEA") {
        $tags.Add("macd")
    }

    return @($tags | Select-Object -Unique)
}

function Get-ImageRecordsForPage(
    [string]$PageUrl,
    [string]$Collection,
    [string]$ArticleKey,
    [object]$ArticleNo
) {
    $html = Get-PageHtml $PageUrl
    if ([string]::IsNullOrWhiteSpace($html)) {
        return @()
    }

    $title = Get-PageTitle $html
    $date = Get-PageDate $html
    $matches = [regex]::Matches($html, "<img\b[^>]*?\bsrc\s*=\s*[""']([^""']+)[""'][^>]*>", "IgnoreCase")
    $records = New-Object System.Collections.Generic.List[object]
    $imageIndex = 0

    foreach ($match in $matches) {
        $src = $match.Groups[1].Value
        $imageUrl = Resolve-Url $PageUrl $src
        if ([string]::IsNullOrWhiteSpace($imageUrl)) {
            continue
        }
        if ($imageUrl -match "myiconslim|ico_mailme|favicon|logo") {
            continue
        }
        if ($imageUrl -notmatch "\.(png|jpg|jpeg|gif|webp)(\?|$)") {
            continue
        }

        $imageIndex++
        $uri = [Uri]$imageUrl
        $originalFileName = Get-SafeFileName ([IO.Path]::GetFileName($uri.LocalPath))
        $extension = [IO.Path]::GetExtension($originalFileName)
        if ([string]::IsNullOrWhiteSpace($extension)) {
            $extension = ".png"
        }
        $baseName = [IO.Path]::GetFileNameWithoutExtension($originalFileName)
        $localFileName = "img_{0:D3}_{1}{2}" -f $imageIndex, $baseName, $extension
        $folder = if ($Collection -eq "original") { "original/$ArticleKey" } else { "illustrated/$ArticleKey" }
        $relativePath = "docs/chan_images/$folder/$localFileName"
        $context = Get-ContextAround $html $match.Index 900
        $tags = Get-Tags $title $context.Before $context.After $Collection $ArticleKey
        $altMatch = [regex]::Match($match.Value, "\balt\s*=\s*[""']([^""']*)[""']", "IgnoreCase")
        $alt = if ($altMatch.Success) { Decode-Html $altMatch.Groups[1].Value } else { "" }

        $recordId = "{0}-{1}-img{2:D3}" -f $Collection, $ArticleKey, $imageIndex
        $records.Add([pscustomobject]@{
            id = $recordId
            collection = $Collection
            articleKey = $ArticleKey
            articleNo = $ArticleNo
            title = $title
            date = $date
            pageUrl = $PageUrl
            imageIndex = $imageIndex
            imageUrl = $imageUrl
            localPath = $relativePath
            originalFileName = $originalFileName
            alt = $alt
            contextBefore = $context.Before
            contextAfter = $context.After
            tags = $tags
        })
    }

    return $records.ToArray()
}

function Get-IllustratedPageLinks {
    $indexUrl = "https://www.kline8.com/chanlun?p=t0"
    $html = Get-PageHtml $indexUrl
    if ([string]::IsNullOrWhiteSpace($html)) {
        return @()
    }

    $links = New-Object System.Collections.Generic.List[string]
    $matches = [regex]::Matches($html, "href\s*=\s*[""']([^""']*p=t\d+)[""']", "IgnoreCase")
    foreach ($match in $matches) {
        $links.Add((Resolve-Url $indexUrl $match.Groups[1].Value))
    }

    return @($links | Select-Object -Unique)
}

$root = (Resolve-Path ".").Path
$outputAbs = Join-Path $root $OutputRoot
if (-not (Test-Path $outputAbs)) {
    New-Item -ItemType Directory -Force -Path $outputAbs | Out-Null
}

$manifest = New-Object System.Collections.Generic.List[object]

Write-Host "Scanning original Chan articles $OriginalStart..$OriginalEnd"
foreach ($i in $OriginalStart..$OriginalEnd) {
    $pageUrl = "https://kline8.com/chanlun?p=$i"
    $articleKey = "p{0:D3}" -f $i
    $records = @(Get-ImageRecordsForPage $pageUrl "original" $articleKey $i)
    foreach ($record in $records) {
        $manifest.Add($record)
    }
    if ($records.Count -gt 0) {
        Write-Host ("  {0}: {1} image(s)" -f $articleKey, $records.Count)
    }
    Start-Sleep -Milliseconds 120
}

Write-Host "Scanning illustrated Chan pages"
$illustratedLinks = @(Get-IllustratedPageLinks)
foreach ($link in $illustratedLinks) {
    $keyMatch = [regex]::Match($link, "p=(t\d+)", "IgnoreCase")
    if (-not $keyMatch.Success) {
        continue
    }
    $articleKey = $keyMatch.Groups[1].Value.ToLowerInvariant()
    $records = @(Get-ImageRecordsForPage $link "illustrated" $articleKey $null)
    foreach ($record in $records) {
        $manifest.Add($record)
    }
    if ($records.Count -gt 0) {
        Write-Host ("  {0}: {1} image(s)" -f $articleKey, $records.Count)
    }
    Start-Sleep -Milliseconds 120
}

$downloaded = 0
$failed = 0
if (-not $SkipDownload) {
    Write-Host "Downloading $($manifest.Count) image(s)"
    foreach ($record in $manifest) {
        $absolutePath = Join-Path $root ($record.localPath -replace "/", [IO.Path]::DirectorySeparatorChar)
        if (Test-Path $absolutePath) {
            $downloaded++
            continue
        }
        if (Save-RemoteFile $record.imageUrl $absolutePath) {
            $downloaded++
        }
        else {
            $failed++
        }
        Start-Sleep -Milliseconds 80
    }
}

$generatedAt = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss zzz")
$sourceSummary = [pscustomobject]@{
    generatedAt = $generatedAt
    source = "kline8.com chanlun original and illustrated pages"
    originalRange = "$OriginalStart..$OriginalEnd"
    illustratedIndex = "https://www.kline8.com/chanlun?p=t0"
    imageCount = $manifest.Count
    downloadedCount = $downloaded
    failedDownloadCount = $failed
    usageNote = "Local research cache for retrieval inside this project; keep source pageUrl and imageUrl attribution."
}

$manifestObject = [pscustomobject]@{
    meta = $sourceSummary
    images = $manifest.ToArray()
}

$manifestPath = Join-Path $outputAbs "manifest.json"
$manifestObject | ConvertTo-Json -Depth 12 | Set-Content -Path $manifestPath -Encoding UTF8

$indexLines = New-Object System.Collections.Generic.List[string]
$indexLines.Add("# Chan Theory Image Index")
$indexLines.Add("")
$indexLines.Add("Generated at: $generatedAt")
$indexLines.Add("")
$indexLines.Add("Source: kline8.com Chan original and illustrated pages. Images are cached locally for retrieval inside this project. Keep pageUrl and imageUrl attribution.")
$indexLines.Add("")
$indexLines.Add("- Image count: $($manifest.Count)")
$indexLines.Add("- Original range: $OriginalStart..$OriginalEnd")
$indexLines.Add("- Illustrated index: https://www.kline8.com/chanlun?p=t0")
$indexLines.Add("- Machine-readable manifest: docs/chan_images/manifest.json")
$indexLines.Add("")

$grouped = $manifest | Group-Object collection, articleKey | Sort-Object Name
foreach ($group in $grouped) {
    $first = $group.Group[0]
    $indexLines.Add("## $($first.collection) / $($first.articleKey) / $($first.title)")
    $indexLines.Add("")
    $indexLines.Add("- Page: $($first.pageUrl)")
    if (-not [string]::IsNullOrWhiteSpace($first.date)) {
        $indexLines.Add("- Date: $($first.date)")
    }
    $indexLines.Add("- Images: $($group.Count)")
    $indexLines.Add("")
    foreach ($item in $group.Group) {
        $tagText = ($item.tags -join ", ")
        $indexLines.Add("### $($item.id)")
        $indexLines.Add("")
        $indexLines.Add("- Local path: $($item.localPath)")
        $indexLines.Add("- Image URL: $($item.imageUrl)")
        $indexLines.Add("- Tags: $tagText")
        if (-not [string]::IsNullOrWhiteSpace($item.contextBefore)) {
            $summary = $item.contextBefore
            if ($summary.Length -gt 180) {
                $summary = $summary.Substring([Math]::Max(0, $summary.Length - 180))
            }
            $indexLines.Add("- Context before: $summary")
        }
        if (-not [string]::IsNullOrWhiteSpace($item.contextAfter)) {
            $summary = $item.contextAfter
            if ($summary.Length -gt 220) {
                $summary = $summary.Substring(0, 220)
            }
            $indexLines.Add("- Context after: $summary")
        }
        $indexLines.Add("")
    }
}

$indexPath = Join-Path $outputAbs "index.md"
$indexLines | Set-Content -Path $indexPath -Encoding UTF8

Write-Host "Done"
Write-Host "Manifest: $manifestPath"
Write-Host "Index: $indexPath"
Write-Host "Images: $($manifest.Count), downloaded: $downloaded, failed: $failed"
