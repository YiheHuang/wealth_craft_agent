param(
    [int]$StartYear = 2018,
    [int]$EndYear = 2026,
    [int]$WindowDays = 90,
    [int]$FutureDays = 120,
    [int]$StepDays = 45,
    [int]$MaxSymbols = 0,
    [string]$OutputRoot = "docs/historical_patterns"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$HttpClient = [System.Net.Http.HttpClient]::new()
$HttpClient.Timeout = [TimeSpan]::FromSeconds(25)
$HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36")

function New-Stock($symbol, $name, $industry, $theme, $weight) {
    [pscustomobject]@{
        Symbol = $symbol
        Name = $name
        Industry = $industry
        Theme = $theme
        Weight = $weight
    }
}

$Universe = @(
    New-Stock "600519" "贵州茅台" "白酒" "高端白酒" 10
    New-Stock "000858" "五粮液" "白酒" "高端白酒" 9
    New-Stock "000568" "泸州老窖" "白酒" "高端白酒" 9
    New-Stock "600809" "山西汾酒" "白酒" "清香白酒" 9
    New-Stock "002304" "洋河股份" "白酒" "次高端白酒" 8
    New-Stock "000596" "古井贡酒" "白酒" "区域白酒" 8
    New-Stock "603369" "今世缘" "白酒" "区域白酒" 8
    New-Stock "000799" "酒鬼酒" "白酒" "弹性白酒" 8
    New-Stock "600702" "舍得酒业" "白酒" "次高端白酒" 8
    New-Stock "600779" "水井坊" "白酒" "次高端白酒" 8
    New-Stock "603589" "口子窖" "白酒" "区域白酒" 7
    New-Stock "600559" "老白干酒" "白酒" "区域白酒" 7
    New-Stock "603198" "迎驾贡酒" "白酒" "区域白酒" 7
    New-Stock "000860" "顺鑫农业" "白酒" "大众酒与农业" 6

    New-Stock "300750" "宁德时代" "新能源" "动力电池" 6
    New-Stock "002594" "比亚迪" "新能源车" "整车与电池" 6
    New-Stock "300014" "亿纬锂能" "新能源" "锂电池" 5
    New-Stock "002812" "恩捷股份" "新能源" "锂电隔膜" 5
    New-Stock "002460" "赣锋锂业" "新能源" "锂资源" 5
    New-Stock "002466" "天齐锂业" "新能源" "锂资源" 5
    New-Stock "300274" "阳光电源" "新能源" "光伏逆变器" 5
    New-Stock "601012" "隆基绿能" "新能源" "光伏组件" 5

    New-Stock "600276" "恒瑞医药" "医药" "创新药" 6
    New-Stock "300760" "迈瑞医疗" "医药" "医疗器械" 6
    New-Stock "300015" "爱尔眼科" "医药" "医疗服务" 5
    New-Stock "000661" "长春高新" "医药" "生长激素" 5
    New-Stock "300122" "智飞生物" "医药" "疫苗" 5
    New-Stock "600436" "片仔癀" "医药" "中药消费" 5
    New-Stock "603259" "药明康德" "医药" "CXO" 5

    New-Stock "600036" "招商银行" "金融" "股份制银行" 5
    New-Stock "601318" "中国平安" "金融" "保险" 5
    New-Stock "601166" "兴业银行" "金融" "股份制银行" 4
    New-Stock "601398" "工商银行" "金融" "国有大行" 4
    New-Stock "601688" "华泰证券" "金融" "券商" 4

    New-Stock "688981" "中芯国际" "半导体" "晶圆代工" 5
    New-Stock "603501" "韦尔股份" "半导体" "CIS芯片" 5
    New-Stock "002371" "北方华创" "半导体" "设备" 5
    New-Stock "300782" "卓胜微" "半导体" "射频芯片" 5
    New-Stock "688012" "中微公司" "半导体" "设备" 5

    New-Stock "600887" "伊利股份" "消费" "乳制品" 5
    New-Stock "000333" "美的集团" "家电" "白电" 5
    New-Stock "000651" "格力电器" "家电" "白电" 5
    New-Stock "603288" "海天味业" "消费" "调味品" 5
    New-Stock "600309" "万华化学" "化工" "MDI" 5
    New-Stock "601899" "紫金矿业" "周期" "有色金属" 5
    New-Stock "601088" "中国神华" "周期" "煤炭高股息" 4
    New-Stock "600019" "宝钢股份" "周期" "钢铁" 4
)

if ($MaxSymbols -gt 0) {
    $Universe = @($Universe | Select-Object -First $MaxSymbols)
}

function Get-MarketPrefix([string]$symbol) {
    if ($symbol.StartsWith("6")) { return "sh" }
    return "sz"
}

function Invoke-TencentKLine([string]$symbol, [int]$year) {
    $ticker = "$(Get-MarketPrefix $symbol)$symbol"
    $start = "{0}-01-01" -f $year
    $end = if ($year -eq $EndYear) { "{0:yyyy-MM-dd}" -f (Get-Date) } else { "{0}-12-31" -f $year }
    $url = "https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?param=$ticker,day,$start,$end,500,qfq"
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $json = $HttpClient.GetStringAsync($url).GetAwaiter().GetResult()
            if ([string]::IsNullOrWhiteSpace($json)) { throw "empty response" }
            $obj = $json | ConvertFrom-Json
            $node = $obj.data.PSObject.Properties[$ticker].Value
            if ($null -eq $node) { return @() }
            $rows = $node.qfqday
            if ($null -eq $rows) { $rows = $node.day }
            if ($null -eq $rows) { return @() }
            return @($rows)
        }
        catch {
            if ($attempt -eq 3) {
                Write-Warning "Fetch failed: $symbol $year $($_.Exception.Message)"
                return @()
            }
            Start-Sleep -Milliseconds (300 * $attempt)
        }
    }
}

function Get-KLines([pscustomobject]$stock) {
    $rows = New-Object System.Collections.Generic.List[object]
    for ($year = $StartYear; $year -le $EndYear; $year++) {
        foreach ($row in (Invoke-TencentKLine $stock.Symbol $year)) {
            if ($row.Count -lt 6) { continue }
            $rows.Add([pscustomobject]@{
                Symbol = $stock.Symbol
                Date = [datetime]::Parse($row[0], [Globalization.CultureInfo]::InvariantCulture)
                Open = [double]::Parse($row[1], [Globalization.CultureInfo]::InvariantCulture)
                Close = [double]::Parse($row[2], [Globalization.CultureInfo]::InvariantCulture)
                High = [double]::Parse($row[3], [Globalization.CultureInfo]::InvariantCulture)
                Low = [double]::Parse($row[4], [Globalization.CultureInfo]::InvariantCulture)
                Volume = [double]::Parse($row[5], [Globalization.CultureInfo]::InvariantCulture)
            })
        }
        Start-Sleep -Milliseconds 120
    }

    return @($rows | Sort-Object Date -Unique)
}

function Get-Pct($current, $basis) {
    if ([math]::Abs($basis) -lt 0.000001) { return 0.0 }
    return (($current - $basis) / $basis) * 100.0
}

function Get-MaxDrawdown([double[]]$closes) {
    if ($closes.Count -eq 0) { return 0.0 }
    $peak = $closes[0]
    $max = 0.0
    foreach ($close in $closes) {
        if ($close -gt $peak) { $peak = $close }
        if ($peak -gt 0) {
            $dd = (($close - $peak) / $peak) * 100.0
            if ($dd -lt $max) { $max = $dd }
        }
    }
    return $max
}

function Get-Volatility([double[]]$closes) {
    if ($closes.Count -lt 2) { return 0.0 }
    $returns = for ($i = 1; $i -lt $closes.Count; $i++) {
        if ($closes[$i - 1] -ne 0) { (($closes[$i] - $closes[$i - 1]) / $closes[$i - 1]) * 100.0 }
    }
    if ($returns.Count -eq 0) { return 0.0 }
    $avg = ($returns | Measure-Object -Average).Average
    $variance = (($returns | ForEach-Object { [math]::Pow($_ - $avg, 2) }) | Measure-Object -Average).Average
    return [math]::Sqrt($variance)
}

function Get-MovingAverage([double[]]$values, [int]$period) {
    $result = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -lt $values.Count; $i++) {
        $start = [math]::Max(0, $i - $period + 1)
        $sum = 0.0
        $count = 0
        for ($j = $start; $j -le $i; $j++) { $sum += $values[$j]; $count++ }
        $result.Add($sum / [math]::Max(1, $count))
    }
    return [double[]]$result.ToArray()
}

function Get-Ema([double[]]$values, [int]$period) {
    $result = New-Object System.Collections.Generic.List[double]
    $multiplier = 2.0 / ($period + 1)
    for ($i = 0; $i -lt $values.Count; $i++) {
        if ($i -eq 0) { $result.Add($values[$i]) }
        else { $result.Add(($values[$i] - $result[$i - 1]) * $multiplier + $result[$i - 1]) }
    }
    return [double[]]$result.ToArray()
}

function Get-MacdState([double[]]$closes) {
    if ($closes.Count -lt 35) { return "insufficient" }
    $ema12 = Get-Ema $closes 12
    $ema26 = Get-Ema $closes 26
    $dif = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -lt $closes.Count; $i++) { $dif.Add($ema12[$i] - $ema26[$i]) }
    $dea = Get-Ema ([double[]]$dif.ToArray()) 9
    $lastDif = $dif[$dif.Count - 1]
    $lastDea = $dea[$dea.Count - 1]
    if ($lastDif -ge 0 -and $lastDif -ge $lastDea) { return "above_zero_strong" }
    if ($lastDif -ge 0 -and $lastDif -lt $lastDea) { return "above_zero_fading" }
    if ($lastDif -lt 0 -and $lastDif -ge $lastDea) { return "below_zero_repair" }
    return "below_zero_weak"
}

function Get-Rsi([double[]]$closes, [int]$period = 14) {
    if ($closes.Count -lt ($period + 1)) { return 50.0 }
    $gains = New-Object System.Collections.Generic.List[double]
    $losses = New-Object System.Collections.Generic.List[double]
    for ($i = $closes.Count - $period; $i -lt $closes.Count; $i++) {
        $diff = $closes[$i] - $closes[$i - 1]
        $gains.Add([math]::Max(0, $diff))
        $losses.Add([math]::Max(0, -$diff))
    }
    $avgGain = ($gains | Measure-Object -Average).Average
    $avgLoss = ($losses | Measure-Object -Average).Average
    if ($avgLoss -eq 0) { return 100.0 }
    $rs = $avgGain / $avgLoss
    return 100.0 - 100.0 / (1.0 + $rs)
}

function Get-FeatureVector([object[]]$window) {
    $closes = [double[]]($window | ForEach-Object { $_.Close })
    $volumes = [double[]]($window | ForEach-Object { $_.Volume })
    $high = ($window | Measure-Object High -Maximum).Maximum
    $low = ($window | Measure-Object Low -Minimum).Minimum
    $first = $closes[0]
    $last = $closes[$closes.Count - 1]
    $firstVolume = (($volumes | Select-Object -First ([math]::Min(20, $volumes.Count))) | Measure-Object -Average).Average
    $lastVolume = (($volumes | Select-Object -Last ([math]::Min(20, $volumes.Count))) | Measure-Object -Average).Average
    $priorVolume = if ($volumes.Count -ge 40) {
        (($volumes | Select-Object -Skip ([math]::Max(0, $volumes.Count - 40)) -First 20) | Measure-Object -Average).Average
    } else { $firstVolume }
    $ma20 = Get-MovingAverage $closes 20
    $ma60 = Get-MovingAverage $closes 60
    $currentMa20 = $ma20[$ma20.Count - 1]
    $currentMa60 = $ma60[$ma60.Count - 1]
    $ma20Past = if ($ma20.Count -gt 20) { $ma20[$ma20.Count - 21] } else { $ma20[0] }
    $ma60Past = if ($ma60.Count -gt 20) { $ma60[$ma60.Count - 21] } else { $ma60[0] }
    $maArrangement = "mixed"
    if ($last -gt $currentMa20 -and $currentMa20 -gt $currentMa60) { $maArrangement = "bullish" }
    elseif ($last -lt $currentMa20 -and $currentMa20 -lt $currentMa60) { $maArrangement = "bearish" }
    $previousLow = (($window | Select-Object -First ([math]::Max(1, $window.Count - 20))) | Measure-Object Low -Minimum).Minimum
    $upDays = 0
    for ($i = 1; $i -lt $closes.Count; $i++) { if ($closes[$i] -ge $closes[$i - 1]) { $upDays++ } }

    [pscustomobject]@{
        returnPct = [math]::Round((Get-Pct $last $first), 2)
        maxDrawdownPct = [math]::Round((Get-MaxDrawdown $closes), 2)
        volatilityPct = [math]::Round((Get-Volatility $closes), 2)
        volumeRatio20d = [math]::Round($(if ($priorVolume -le 0) { 1.0 } else { $lastVolume / $priorVolume }), 2)
        volumeTrendPct = [math]::Round($(if ($firstVolume -le 0) { 0.0 } else { (($lastVolume - $firstVolume) / $firstVolume) * 100.0 }), 2)
        ma20SlopePct = [math]::Round($(if ($ma20Past -le 0) { 0.0 } else { (($currentMa20 - $ma20Past) / $ma20Past) * 100.0 }), 2)
        ma60SlopePct = [math]::Round($(if ($ma60Past -le 0) { 0.0 } else { (($currentMa60 - $ma60Past) / $ma60Past) * 100.0 }), 2)
        maArrangement = $maArrangement
        macdState = Get-MacdState $closes
        rsi14 = [math]::Round((Get-Rsi $closes 14), 2)
        closeNearLowPct = [math]::Round($(if ($high -le $low) { 50.0 } else { (($last - $low) / ($high - $low)) * 100.0 }), 2)
        breakPreviousLow = [bool]($last -le $previousLow)
        upDays = $upDays
        downDays = [math]::Max(0, $closes.Count - 1 - $upDays)
    }
}

function Get-FutureOutcome([object[]]$all, [int]$windowEndIndex) {
    $baseClose = $all[$windowEndIndex].Close
    $future = @($all | Select-Object -Skip ($windowEndIndex + 1) -First $FutureDays)
    if ($future.Count -lt $FutureDays) { return $null }
    $closeAt = {
        param([int]$n)
        if ($future.Count -ge $n) { return $future[$n - 1].Close }
        return $future[$future.Count - 1].Close
    }
    $first60 = @($future | Select-Object -First 60)
    $lowest60 = ($first60 | Measure-Object Low -Minimum).Minimum
    $highest60 = ($first60 | Measure-Object High -Maximum).Maximum
    [pscustomobject]@{
        return20dPct = [math]::Round((Get-Pct (& $closeAt 20) $baseClose), 2)
        return60dPct = [math]::Round((Get-Pct (& $closeAt 60) $baseClose), 2)
        return120dPct = [math]::Round((Get-Pct (& $closeAt 120) $baseClose), 2)
        maxDrawdownNext60dPct = [math]::Round((Get-Pct $lowest60 $baseClose), 2)
        maxReboundNext60dPct = [math]::Round((Get-Pct $highest60 $baseClose), 2)
        newLowWithin60d = [bool]($lowest60 -lt (($all | Select-Object -Index $windowEndIndex).Low))
    }
}

function Get-Labels($features, $outcome) {
    $labels = New-Object System.Collections.Generic.List[string]
    if ($features.returnPct -le -20) { $labels.Add("深度回撤") }
    elseif ($features.returnPct -le -10) { $labels.Add("中等回撤") }
    elseif ($features.returnPct -ge 20) { $labels.Add("强趋势上涨") }
    elseif ($features.returnPct -ge 10) { $labels.Add("阶段上涨") }
    if ($features.maxDrawdownPct -le -25) { $labels.Add("大幅回撤") }
    if ($features.maArrangement -eq "bearish") { $labels.Add("均线空头") }
    if ($features.maArrangement -eq "bullish") { $labels.Add("均线多头") }
    if ($features.breakPreviousLow) { $labels.Add("跌破前低") }
    if ($features.closeNearLowPct -le 15) { $labels.Add("贴近区间低位") }
    if ($features.closeNearLowPct -ge 85) { $labels.Add("贴近区间高位") }
    if ($features.volumeRatio20d -ge 1.5) { $labels.Add("近期放量") }
    if ($features.volumeRatio20d -le 0.75) { $labels.Add("近期缩量") }
    if ($features.rsi14 -le 35) { $labels.Add("RSI偏低") }
    if ($features.macdState -like "below_zero*") { $labels.Add("MACD零轴下方") }
    if ($outcome.newLowWithin60d) { $labels.Add("后续再创新低") }
    if ($outcome.return60dPct -ge 10) { $labels.Add("后续显著反弹") }
    if ($outcome.return60dPct -le -10) { $labels.Add("后续继续走弱") }
    return @($labels | Select-Object -Unique)
}

function Get-PatternType($features, $outcome) {
    if ($features.returnPct -le -8 -and $outcome.newLowWithin60d -and $outcome.return60dPct -lt 3) { return "下跌中继" }
    if ($features.returnPct -le -8 -and -not $outcome.newLowWithin60d -and $outcome.return60dPct -gt 8) { return "阶段底部" }
    if ($outcome.maxReboundNext60dPct -gt 12 -and $outcome.return120dPct -lt 0) { return "弱反弹" }
    if ($features.returnPct -ge 15 -and $features.closeNearLowPct -gt 75) { return "高位强势" }
    if ([math]::Abs($features.returnPct) -lt 8 -and $features.volatilityPct -lt 3) { return "震荡修复" }
    return "趋势延续观察"
}

function Get-Regime([datetime]$date, [string]$industry) {
    $y = $date.Year
    if ($y -eq 2018) { return "2018年去杠杆与风险偏好收缩，A股整体回撤明显" }
    if ($y -eq 2019) { return "2019年核心资产重估，消费与成长均出现结构性行情" }
    if ($y -eq 2020) { return "2020年疫情冲击后流动性宽松，结构性修复与分化并存" }
    if ($y -eq 2021) { return "2021年核心资产估值收缩，消费、医药、新能源高波动" }
    if ($y -eq 2022) { return "2022年宏观压力与行业景气切换，市场波动放大" }
    if ($y -eq 2023) { return "2023年存量博弈与结构分化，弱修复行情较多" }
    if ($y -eq 2024) { return "2024年高股息与成长波动并存，风险偏好分层" }
    if ($y -ge 2025) { return "2025-2026年存量资金博弈，行业内部分化延续" }
    return "$industry 历史结构样本"
}

function Get-StructureSummary($stock, $features, $patternType) {
    "该90日窗口属于$($stock.Industry) / $($stock.Theme) 样本，区间涨跌幅 $($features.returnPct)%、最大回撤 $($features.maxDrawdownPct)%、收盘位置距离区间低位 $($features.closeNearLowPct)%。结构标签偏向「$patternType」，均线状态为 $($features.maArrangement)，MACD状态为 $($features.macdState)。"
}

function Get-VolumeSummary($features) {
    if ($features.volumeRatio20d -ge 1.5) { return "窗口末端成交量较前20日明显放大，说明多空分歧或抛压释放增强。" }
    if ($features.volumeRatio20d -le 0.75) { return "窗口末端成交量较前20日收缩，说明反弹或下跌过程中的参与度下降。" }
    return "窗口末端量能相对平稳，未形成特别极端的放量或缩量特征。"
}

function Get-RiskInterpretation($patternType, $outcome) {
    switch ($patternType) {
        "阶段底部" { return "相似样本后续未快速创新低，60日收益中枢偏正，但仍需等待结构确认，不能直接等同于已经反转。" }
        "下跌中继" { return "相似样本后续再创新低概率较高，说明跌幅本身不足以构成底部证据，需警惕弱反弹后的二次回落。" }
        "弱反弹" { return "相似样本容易出现阶段反弹，但120日后仍可能回落，适合观察压力位而非直接外推趋势反转。" }
        "高位强势" { return "相似样本处于强势区间，但若后续量价背离或跌破短期均线，回撤会快速放大。" }
        default { return "相似样本后续分布较分散，应结合支撑、量能和均线修复情况动态判断。" }
    }
}

function Get-Lesson($patternType) {
    switch ($patternType) {
        "阶段底部" { return "阶段底部不是由跌幅决定，而是由不再创新低、量能改善、关键均线或平台收复共同确认。" }
        "下跌中继" { return "下跌中继最容易被误判为跌多反弹，必须检查反弹是否能收复压力位以及是否重新放量。" }
        "弱反弹" { return "弱反弹案例提示：短期反弹并不等于趋势修复，需要跟踪反弹后的回落幅度和成交量。" }
        "高位强势" { return "高位强势案例提示：趋势延续阶段要关注波动放大和高位放量后的筹码交换。" }
        default { return "震荡或趋势延续样本需要结合市场环境和相对强弱，避免只凭单一指标做判断。" }
    }
}

function Get-AvoidSaying($patternType) {
    $base = @("历史相似不等于未来必然重复", "不能只凭单个指标下结论")
    if ($patternType -eq "阶段底部") { return $base + @("已经确认见底", "可以忽略后续创新低风险") }
    if ($patternType -eq "下跌中继") { return $base + @("跌多了必然反弹", "支撑位一定有效") }
    if ($patternType -eq "弱反弹") { return $base + @("反弹就是反转", "短期上涨已经改变长期趋势") }
    return $base + @("必然上涨", "必然下跌")
}

function Get-InterestingScore($features, $outcome, $patternType) {
    $score = [math]::Abs($features.returnPct) * 1.3 + [math]::Abs($features.maxDrawdownPct) * 1.2 + [math]::Abs($outcome.return60dPct) + [math]::Abs($outcome.maxDrawdownNext60dPct)
    if ($features.breakPreviousLow) { $score += 8 }
    if ($outcome.newLowWithin60d) { $score += 8 }
    if ($features.volumeRatio20d -ge 1.5 -or $features.volumeRatio20d -le 0.75) { $score += 4 }
    if ($patternType -in @("阶段底部", "下跌中继", "弱反弹")) { $score += 8 }
    return $score
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$AllCases = New-Object System.Collections.Generic.List[object]
$Failures = New-Object System.Collections.Generic.List[object]

foreach ($stock in $Universe) {
    Write-Host "Fetching $($stock.Symbol) $($stock.Name) ..."
    $klines = @(Get-KLines $stock)
    if ($klines.Count -lt ($WindowDays + $FutureDays + 20)) {
        $Failures.Add([pscustomobject]@{ symbol = $stock.Symbol; name = $stock.Name; reason = "insufficient kline count $($klines.Count)" })
        continue
    }

    $candidateCases = New-Object System.Collections.Generic.List[object]
    for ($start = 0; $start -le ($klines.Count - $WindowDays - $FutureDays); $start += $StepDays) {
        $endIndex = $start + $WindowDays - 1
        $window = @($klines | Select-Object -Skip $start -First $WindowDays)
        $future = Get-FutureOutcome $klines $endIndex
        if ($null -eq $future) { continue }
        $features = Get-FeatureVector $window
        $patternType = Get-PatternType $features $future
        $labels = Get-Labels $features $future
        $windowStart = $window[0].Date
        $windowEnd = $window[$window.Count - 1].Date
        $caseId = "{0}_{1}_{2}_{3}d" -f $stock.Symbol, $windowStart.ToString("yyyyMMdd"), $windowEnd.ToString("yyyyMMdd"), $WindowDays
        $interesting = Get-InterestingScore $features $future $patternType
        $candidateCases.Add([pscustomobject]@{
            score = $interesting
            case = [pscustomobject]@{
                caseId = $caseId
                title = "$($stock.Name) $($windowStart.ToString('yyyy-MM')) 至 $($windowEnd.ToString('yyyy-MM')) $patternType 样本"
                symbol = $stock.Symbol
                name = $stock.Name
                industry = $stock.Industry
                theme = $stock.Theme
                marketRegime = Get-Regime $windowEnd $stock.Industry
                windowStart = $windowStart.ToString("yyyy-MM-dd")
                windowEnd = $windowEnd.ToString("yyyy-MM-dd")
                windowDays = $WindowDays
                patternType = $patternType
                patternLabels = $labels
                structureSummary = Get-StructureSummary $stock $features $patternType
                volumeSummary = Get-VolumeSummary $features
                riskInterpretation = Get-RiskInterpretation $patternType $future
                futureOutcome = $future
                lesson = Get-Lesson $patternType
                avoidSaying = Get-AvoidSaying $patternType
                features = $features
                dataSource = "Tencent qfq daily kline"
            }
        })
    }

    $selected = $candidateCases |
        Sort-Object score -Descending |
        Group-Object { $_.case.patternType } |
        ForEach-Object { $_.Group | Select-Object -First ([math]::Ceiling($stock.Weight / 3)) } |
        Sort-Object score -Descending |
        Select-Object -First $stock.Weight

    foreach ($item in $selected) {
        $AllCases.Add($item.case)
    }
}

$cases = @($AllCases | Sort-Object industry, symbol, windowEnd)
$manifest = [pscustomobject]@{
    generatedAt = (Get-Date).ToString("s")
    source = "Tencent qfq daily kline"
    startYear = $StartYear
    endYear = $EndYear
    windowDays = $WindowDays
    futureDays = $FutureDays
    universeCount = $Universe.Count
    caseCount = $cases.Count
    industryCounts = @($cases | Group-Object industry | Sort-Object Count -Descending | ForEach-Object { [pscustomobject]@{ industry = $_.Name; count = $_.Count } })
    failures = $Failures
    cases = $cases
}

$jsonPath = Join-Path $OutputRoot "cases.json"
$manifest | ConvertTo-Json -Depth 12 | Set-Content -Path $jsonPath -Encoding UTF8

$index = New-Object System.Text.StringBuilder
[void]$index.AppendLine("# Historical Pattern Case Library")
[void]$index.AppendLine()
[void]$index.AppendLine("- Generated at: $($manifest.generatedAt)")
[void]$index.AppendLine("- Source: $($manifest.source)")
[void]$index.AppendLine("- Window: $WindowDays trading days; future validation: $FutureDays trading days")
[void]$index.AppendLine("- Universe count: $($Universe.Count)")
[void]$index.AppendLine("- Case count: $($cases.Count)")
[void]$index.AppendLine()
[void]$index.AppendLine("## Industry Coverage")
foreach ($group in $manifest.industryCounts) {
    [void]$index.AppendLine("- $($group.industry): $($group.count)")
}
[void]$index.AppendLine()
[void]$index.AppendLine("## Top Cases")
foreach ($case in ($cases | Select-Object -First 80)) {
    [void]$index.AppendLine("- $($case.caseId) | $($case.title) | $($case.industry) | $($case.patternType) | next60=$($case.futureOutcome.return60dPct)% | newLow60=$($case.futureOutcome.newLowWithin60d)")
}
$indexPath = Join-Path $OutputRoot "index.md"
$index.ToString() | Set-Content -Path $indexPath -Encoding UTF8

Write-Host "Generated $($cases.Count) historical pattern cases -> $jsonPath"
if ($Failures.Count -gt 0) {
    Write-Warning "Failures: $($Failures.Count)"
}
