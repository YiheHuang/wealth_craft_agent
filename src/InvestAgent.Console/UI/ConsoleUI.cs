using InvestAgent.Core.Models;
using Spectre.Console;

namespace InvestAgent.Console.UI;

/// <summary>
/// 控制台用户界面。
/// 使用 Spectre.Console 库提供富文本终端交互体验——
/// 包括欢迎界面、帮助系统、步骤渲染和 K 线图可视化。
/// 作为 Agent 执行循环的消费者，通过事件订阅接收每一步的实时更新。
/// </summary>
public class ConsoleUI
{
    private readonly InvestAgent.Core.Agent.InvestAgentLoop _agent;

    public ConsoleUI(InvestAgent.Core.Agent.InvestAgentLoop agent)
    {
        _agent = agent;
    }

    /// <summary>启动主交互循环——输入/处理/输出</summary>
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

            // 退出命令
            if (userInput.Trim().ToLower() is "quit" or "exit")
            {
                AnsiConsole.MarkupLine("[green]感谢使用 InvestAgent，祝投资顺利！[/]");
                break;
            }

            // 帮助命令
            if (userInput.Trim().ToLower() is "help")
            {
                ShowHelp();
                continue;
            }

            // 清屏命令
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

    /// <summary>处理单次查询：回显问题 → 执行 Agent 循环 → 渲染步骤</summary>
    private async Task ProcessQueryAsync(string userInput)
    {
        // 回显用户问题
        var panel = new Panel(new Markup($"[grey]{userInput.EscapeMarkup()}[/]"))
        {
            Header = new PanelHeader("[bold]您的问题[/]"),
            Border = BoxBorder.Rounded,
            Expand = true
        };
        AnsiConsole.Write(panel);

        var steps = new List<AgentStep>();

        // 订阅步骤事件
        Action<AgentStep> handler = step =>
        {
            steps.Add(step);
            RenderStep(step); // 即时渲染每一步
        };

        _agent.OnStep += handler;
        try
        {
            await foreach (var step in _agent.RunAsync(userInput))
            {
                // 步骤通过事件记录，此处仅消费迭代器
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

    /// <summary>根据步骤类型渲染不同的终端输出格式</summary>
    private void RenderStep(AgentStep step)
    {
        switch (step.Type)
        {
            case AgentStepType.Thought:
                // 思考阶段——灰色弱显
                AnsiConsole.MarkupLine($"[dim][[Thought 第{step.StepNumber}步]][/] {Markup.Escape(step.Content)}");
                break;

            case AgentStepType.Action:
                // 工具调用——蓝色高亮
                var argsPreview = step.FunctionArgs?.Length > 100
                    ? step.FunctionArgs[..100] + "..."
                    : step.FunctionArgs ?? "";
                AnsiConsole.MarkupLine($"[blue][[Action]][/] [bold]{Markup.Escape(step.FunctionName ?? "")}[/]({Markup.Escape(argsPreview)})");
                break;

            case AgentStepType.Observation:
                // 观察结果——可能包含 K 线图渲染
                RenderObservation(step);
                break;

            case AgentStepType.Response:
                // 最终响应——绿色面板
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

    /// <summary>渲染 Observation 步骤——K 线数据尝试绘制图表，其他数据以面板展示</summary>
    private void RenderObservation(AgentStep step)
    {
        if (string.IsNullOrEmpty(step.FunctionResult))
        {
            AnsiConsole.MarkupLine("[grey][[Observation]][/] 无返回数据");
            return;
        }

        AnsiConsole.MarkupLine($"[grey][[Observation]][/] {Markup.Escape(step.FunctionName ?? "")} 返回数据:");

        // 检测是否为 K 线数据函数，若是则渲染蜡烛图
        var funcName = step.FunctionName ?? "";
        if ((funcName.EndsWith("get_historical_prices") || funcName.EndsWith("get_monthly_kline"))
            && KLineChartRenderer.TryParseKLineJson(step.FunctionResult, out var klineData))
        {
            var chartTitle = funcName.EndsWith("get_monthly_kline") ? "月K线图" : "日K线图";
            KLineChartRenderer.Render(klineData, chartTitle);
            return;
        }

        // 普通数据——截断后以 ASCII 面板展示
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

    /// <summary>显示应用欢迎界面——包含功能列表和示例问题</summary>
    private void ShowWelcome()
    {
        var title = new FigletText("InvestAgent")
            .Centered()
            .Color(Color.Green);

        AnsiConsole.Write(title);

        var grid = new Grid();
        grid.AddColumn(); grid.AddColumn();
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
