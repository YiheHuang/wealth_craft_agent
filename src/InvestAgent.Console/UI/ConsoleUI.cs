using InvestAgent.Core.Models;
using Spectre.Console;

namespace InvestAgent.Console.UI;

public class ConsoleUI
{
    private readonly InvestAgent.Core.Agent.InvestAgentLoop _agent;

    public ConsoleUI(InvestAgent.Core.Agent.InvestAgentLoop agent)
    {
        _agent = agent;
    }

    public async Task RunAsync()
    {
        ShowWelcome();

        while (true)
        {
            var userInput = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold yellow]请输入您的问题:[/]")
                    .PromptStyle("grey")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(userInput)) continue;

            if (userInput.Trim().ToLower() is "quit" or "exit")
            {
                AnsiConsole.MarkupLine("[green]感谢使用 InvestAgent，祝投资顺利！[/]");
                break;
            }

            if (userInput.Trim().ToLower() is "help")
            {
                ShowHelp();
                continue;
            }

            if (userInput.Trim().ToLower() is "clear")
            {
                AnsiConsole.Clear();
                ShowWelcome();
                continue;
            }

            await ProcessQueryAsync(userInput);
            AnsiConsole.WriteLine();
        }
    }

    private async Task ProcessQueryAsync(string userInput)
    {
        var panel = new Panel(new Markup($"[grey]{userInput.EscapeMarkup()}[/]"))
        {
            Header = new PanelHeader("[bold]您的问题[/]"),
            Border = BoxBorder.Rounded,
            Expand = true
        };
        AnsiConsole.Write(panel);

        var steps = new List<AgentStep>();
        Action<AgentStep> handler = step =>
        {
            steps.Add(step);
            RenderStep(step);
        };

        _agent.OnStep += handler;
        try
        {
            await foreach (var step in _agent.RunAsync(userInput))
            {
                // steps are recorded via event
            }
        }
        finally
        {
            _agent.OnStep -= handler;
        }

        var actionCount = steps.Count(s => s.Type == AgentStepType.Action);
        if (actionCount > 0)
        {
            AnsiConsole.MarkupLine($"[grey]共执行 {actionCount} 次工具调用[/]");
        }
    }

    private void RenderStep(AgentStep step)
    {
        switch (step.Type)
        {
            case AgentStepType.Thought:
                AnsiConsole.MarkupLine($"[dim][[Thought 第{step.StepNumber}步]][/] {Markup.Escape(step.Content)}");
                break;

            case AgentStepType.Action:
                var argsPreview = step.FunctionArgs?.Length > 100
                    ? step.FunctionArgs[..100] + "..."
                    : step.FunctionArgs ?? "";
                AnsiConsole.MarkupLine($"[blue][[Action]][/] [bold]{Markup.Escape(step.FunctionName ?? "")}[/]({Markup.Escape(argsPreview)})");
                break;

            case AgentStepType.Observation:
                RenderObservation(step);
                break;

            case AgentStepType.Response:
                var responsePanel = new Panel(new Markup(Markup.Escape(step.Content)))
                {
                    Header = new PanelHeader("[bold green]分析报告[/]"),
                    Border = BoxBorder.Rounded,
                    Expand = true
                };
                AnsiConsole.Write(responsePanel);
                break;
        }
    }

    private void RenderObservation(AgentStep step)
    {
        if (string.IsNullOrEmpty(step.FunctionResult))
        {
            AnsiConsole.MarkupLine("[grey][[Observation]][/] 无返回数据");
            return;
        }

        AnsiConsole.MarkupLine($"[grey][[Observation]][/] {Markup.Escape(step.FunctionName ?? "")} 返回数据:");

        // 如果是K线数据, 渲染蜡烛图 (SK可能返回 "StockPrice-get_historical_prices" 格式)
        var funcName = step.FunctionName ?? "";
        if ((funcName.EndsWith("get_historical_prices") || funcName.EndsWith("get_monthly_kline"))
            && KLineChartRenderer.TryParseKLineJson(step.FunctionResult, out var klineData))
        {
            var chartTitle = funcName.EndsWith("get_monthly_kline") ? "月K线图" : "日K线图";
            KLineChartRenderer.Render(klineData, chartTitle);
            return;
        }

        var display = step.FunctionResult.Length > 1500
            ? step.FunctionResult[..1500] + "\n...(数据过长,已截断)"
            : step.FunctionResult;

        var resultPanel = new Panel(new Text(display, new Style(Color.Grey)))
        {
            Border = BoxBorder.Ascii,
            BorderStyle = new Style(Color.DarkSlateGray1)
        };
        AnsiConsole.Write(resultPanel);
    }

    private void ShowWelcome()
    {
        var title = new FigletText("InvestAgent")
            .Centered()
            .Color(Color.Green);

        AnsiConsole.Write(title);

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow("引擎:", ".NET 10 + Semantic Kernel");
        grid.AddRow("模型:", "yunwu.ai (OpenAI 兼容)");
        grid.AddRow("数据:", "东方财富实时行情 + 公告");

        var welcomePanel = new Panel(grid)
        {
            Header = new PanelHeader("[bold]InvestAgent - 智能投资研究 AI 助手[/]"),
            Border = BoxBorder.Double,
            Expand = true
        };
        AnsiConsole.Write(welcomePanel);

        AnsiConsole.MarkupLine("\n[bold]可用功能:[/]");
        AnsiConsole.MarkupLine("  * 股票实时行情查询");
        AnsiConsole.MarkupLine("  * 财务报告与估值分析");
        AnsiConsole.MarkupLine("  * 技术指标计算 (MA/RSI/MACD)");
        AnsiConsole.MarkupLine("  * 市场新闻与情绪分析");
        AnsiConsole.MarkupLine("  * 综合投资建议");

        AnsiConsole.MarkupLine("\n[dim]输入 [bold]help[/] 查看帮助, [bold]quit[/] 退出, [bold]clear[/] 清屏[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]示例问题:[/]");
        AnsiConsole.MarkupLine("[dim]  * 分析一下贵州茅台(600519)的投资价值[/]");
        AnsiConsole.MarkupLine("[dim]  * 对比宁德时代(300750)和比亚迪(002594)[/]");
        AnsiConsole.MarkupLine("[dim]  * 帮我看看招商银行的技术面[/]");
        AnsiConsole.MarkupLine("[dim]  * 苹果(AAPL)最近有什么重要新闻?[/]");
        AnsiConsole.WriteLine();
    }

    private static void ShowHelp()
    {
        AnsiConsole.MarkupLine("[bold]帮助[/]");
        AnsiConsole.MarkupLine("  InvestAgent 是一个智能投资研究助手，您可以:");
        AnsiConsole.MarkupLine("  * 输入自然语言问题进行投资分析");
        AnsiConsole.MarkupLine("  * Agent 会自动查询数据和分析指标");
        AnsiConsole.MarkupLine("  * 支持 A 股和美股股票代码");
        AnsiConsole.MarkupLine("  * 常用命令: [bold]help[/] 帮助, [bold]quit[/] 退出, [bold]clear[/] 清屏");
        AnsiConsole.WriteLine();
    }
}
