# wealth_craft_agent

一个面向 A 股场景的多代理（A/B/C/D）投资分析工具，基于 .NET 10 开发，当前提供 Windows 原生桌面端（WPF）。

## 功能概览

- 输入股票代码或股票名称（仅支持这两类输入）
- 自动拉取并展示：
  - 近一年月 K
  - 近三个月日 K
  - 最近三个月公司新闻与行业新闻
  - 最近一年财务序列
- 多代理分析流程：
  - Agent B：K 线分析
  - Agent C：新闻分析（含积极/消极新闻列表与 URL）
  - Agent D：财务分析
  - Agent A：补充主营业务 + 最终风险提示与投资建议（保留 B/C/D 原文）
- 本地分析历史回放（同一股票保留最新记录）

## 技术栈

- .NET 10
- WPF（桌面端）
- Microsoft Semantic Kernel
- 数据源（组合模式）：
  - Yahoo / 东方财富（A 股优先）
  - Alpha Vantage（A 股新闻默认不使用）

## 项目结构

```text
src/
  InvestAgent.Core/      # 核心 Agent、插件、数据服务
  InvestAgent.Desktop/   # WPF 桌面端
  InvestAgent.Console/   # CLI 版本（调试回归用）
  InvestAgent.Tests/     # 测试
```

## 环境要求

- Windows
- .NET SDK 10

## 快速开始

1. 克隆项目

```powershell
git clone https://github.com/YiheHuang/wealth_craft_agent.git
cd wealth_craft_agent
```

2. 配置环境变量

```powershell
copy .env.example .env
```

按需填写 `.env` 中的 key（`OPENAI_API_KEY`、`ALPHAVANTAGE_API_KEY`、`FINNHUB_API_KEY` 等）。

3. 还原与运行（桌面端）

```powershell
dotnet restore InvestAgent.slnx
dotnet run --project src/InvestAgent.Desktop
```

## 历史记录

- 存储位置：`%LOCALAPPDATA%\InvestAgent\analysis_history.db`
- 同一股票会被最新分析覆盖

如需清空历史（先关闭桌面程序）：

```powershell
Remove-Item "$env:LOCALAPPDATA\InvestAgent\analysis_history.db" -Force
```

## 注意事项

- 当前输入框仅允许：
  - `600519` 这类股票代码
  - `贵州茅台` 这类股票名称
- 最终输出尾句固定为：
  - `⚠️ 以上分析仅供参考`

## 免责声明

本项目仅用于学习与研究，不构成任何投资建议。
