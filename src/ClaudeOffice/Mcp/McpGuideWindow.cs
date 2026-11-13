using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Mcp;

/// <summary>
/// Claude 데스크톱(채팅 및 프로젝트) 연동 상세 가이드 창.
/// 실시간 캡처 이미지, Step 1 ~ Step 5 단계별 안내, 자연스러운 MCP 상태 카드, 지침 프롬프트 원클릭 복사, MCP 등록/해제 기능을 제공한다.
/// </summary>
internal sealed class McpGuideWindow : Window
{

    private readonly Border _statusBadgePill;
    private readonly TextBlock _statusBadgeText;
    private readonly TextBlock _statusDescText;
    private readonly StackPanel _pathDetailPanel;
    private readonly TextBlock _pathListText;
    private readonly Button _togglePathBtn;
    private readonly TextBox _promptBox;
    private readonly Button _copyBtn;
    private readonly TextBlock _copyFeedback;
    private readonly Button _uninstallBtn;

    public McpGuideWindow(Window? owner = null)
    {
        Title = L.T("Claude 데스크톱(채팅 및 프로젝트) 연동 가이드", "Claude Desktop (Chat & Projects) Integration Guide");
        Width = 720;
        Height = 840;
        MinWidth = 600;
        MinHeight = 600;
        ShowInTaskbar = true;

        try
        {
            if (owner != null && owner.IsVisible && PresentationSource.FromVisual(owner) != null)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        catch
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1b)); // Dark zinc
        Foreground = new SolidColorBrush(Color.FromRgb(0xf4, 0xf4, 0xf5));

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Scroll Body
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

        // ------------------ Header ------------------
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3d)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(22, 16, 22, 16)
        };
        var headerStack = new StackPanel();

        var titleBlock = new TextBlock
        {
            Text = L.T("🤖 Claude 데스크톱 연동 가이드", "🤖 Claude Desktop Integration Guide"),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        };
        headerStack.Children.Add(titleBlock);

        var subtitleBlock = new TextBlock
        {
            Text = L.T("Claude 데스크톱의 프로젝트 세션을 사무실 화면에 출근시키고, 실시간으로 양방향 업무를 지시합니다.",
                       "Connect Claude Desktop project sessions to your pixel office with real-time bidirectional instructions."),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xa1, 0xa1, 0xaa)),
            Margin = new Thickness(0, 4, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        headerStack.Children.Add(subtitleBlock);

        // Natural, Modern MCP Status Card
        var statusCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3f, 0x3f, 0x4a)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12)
        };
        var statusCardStack = new StackPanel();

        var statusTopRow = new Grid();
        statusTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var cardTitle = new TextBlock
        {
            Text = L.T("연동 상태", "Connection Status"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(cardTitle);

        _statusBadgePill = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusBadgeText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.Bold
        };
        _statusBadgePill.Child = _statusBadgeText;
        titleRow.Children.Add(_statusBadgePill);
        Grid.SetColumn(titleRow, 0);
        statusTopRow.Children.Add(titleRow);

        _togglePathBtn = new Button
        {
            Content = L.T("설정 경로 보기 [클릭]", "View config paths"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(4, 2, 4, 2)
        };
        _togglePathBtn.Click += (_, _) => TogglePathView();
        Grid.SetColumn(_togglePathBtn, 1);
        statusTopRow.Children.Add(_togglePathBtn);
        statusCardStack.Children.Add(statusTopRow);

        _statusDescText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd8)),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        };
        statusCardStack.Children.Add(_statusDescText);

        _pathDetailPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        var pathHeader = new TextBlock
        {
            Text = L.T("등록된 Claude Desktop 설정 파일:", "Registered Claude Desktop config files:"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xa1, 0xa1, 0xaa)),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        };
        _pathDetailPanel.Children.Add(pathHeader);

        _pathListText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xbd, 0xf8)),
            FontFamily = new FontFamily("Consolas, Malgun Gothic"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 16
        };
        _pathDetailPanel.Children.Add(_pathListText);
        statusCardStack.Children.Add(_pathDetailPanel);

        statusCard.Child = statusCardStack;
        headerStack.Children.Add(statusCard);

        header.Child = headerStack;
        Grid.SetRow(header, 0);
        rootGrid.Children.Add(header);

        // ------------------ Scrollable Body ------------------
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(22, 16, 22, 16)
        };

        var bodyStack = new StackPanel();

        // Step 1
        bodyStack.Children.Add(CreateStepCard(
            1,
            L.T("Claude 데스크톱 앱 완전히 재시작 (최초 1회 필수)", "Restart Claude Desktop App (First Time Only)"),
            L.T("Pixel Office MCP 서버 설정이 이미 컴퓨터에 등록되어 있습니다.\n화면 우측 하단 시스템 트레이(시계 옆)에서 Claude 아이콘 우클릭 → '종료' 또는 'Quit'을 눌러 완전히 종료 후 다시 실행하세요.\n※ Claude 데스크톱은 처음 켜질 때 설정 파일에서 MCP 도구를 불러옵니다.",
                "Pixel Office MCP configuration is registered in Claude Desktop.\nExit Claude Desktop completely (Right-click Claude tray icon → Quit) and restart it.\n※ Claude Desktop loads MCP servers on startup.")
        ));

        // Step 2 (With Captured Screenshot!)
        var step2Card = CreateStepCardHeader(
            2,
            L.T("Claude 데스크톱에서 [프로젝트] 선택 후 [지침] [편집] 클릭", "Select Project in Claude Desktop & click [Edit] on [Instructions]")
        );
        var step2Stack = (StackPanel)step2Card.Child;

        var step2Desc = new TextBlock
        {
            Text = L.T("1. Claude 데스크톱 앱 왼쪽 메뉴에서 연동할 프로젝트(예: '과학공부어플' 등)를 선택합니다.\n2. 메인 화면 오른쪽 패널의 '지침(Instructions)' 항목 옆 [편집] 또는 [추가] 버튼을 클릭합니다.\n(일반 채팅의 경우, 아래 지침 문구를 복사하여 새 대화창의 첫 메시지에 함께 보내도 연동됩니다)",
                       "1. In Claude Desktop, select your Project from the left sidebar.\n2. On the right panel, click [Edit] or [Add] next to 'Instructions'.\n(For regular chat, you can paste the instructions into your first message)"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd8)),
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        step2Stack.Children.Add(step2Desc);
        step2Stack.Children.Add(CreateGuideImage("claude_project_guide.png"));
        bodyStack.Children.Add(step2Card);

        // Step 3 (With Instruction Modal Screenshot & Copy Box)
        var step3Card = CreateStepCardHeader(
            3,
            L.T("아래 연동 지침 문구를 복사하여 붙여넣고 [저장]", "Copy and Paste Instructions into Project & Save")
        );
        var step3Stack = (StackPanel)step3Card.Child;

        var step3Desc = new TextBlock
        {
            Text = L.T("아래 [📋 지침 문구 복사하기] 버튼을 누른 후, 프로젝트 지침 창에 붙여넣기(Ctrl+V)하고 [저장]하세요. 이 프로젝트의 모든 대화에 자동 적용됩니다.",
                       "Click [📋 Copy Instructions Prompt] below, paste it into the Project Instructions field (Ctrl+V), then click Save:"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd8)),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        step3Stack.Children.Add(step3Desc);
        step3Stack.Children.Add(CreateGuideImage("claude_project_step3.png"));

        _promptBox = new TextBox
        {
            Text = McpSetup.GetInstructionsPrompt(),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 145,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1b)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xbd, 0xf8)), // Sky blue text
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3f, 0x3f, 0x46)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            FontFamily = new FontFamily("Consolas, Malgun Gothic, Segoe UI"),
            FontSize = 11
        };
        step3Stack.Children.Add(_promptBox);

        var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        _copyBtn = new Button
        {
            Content = L.T("📋 지침 문구 복사하기", "📋 Copy Instructions Prompt"),
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(Color.FromRgb(0x4f, 0x46, 0xe5)), // Indigo
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _copyBtn.Click += (_, _) => CopyPrompt();
        copyRow.Children.Add(_copyBtn);

        _copyFeedback = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Color.FromRgb(0x4a, 0xde, 0x80)), // Green
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            FontWeight = FontWeights.SemiBold
        };
        copyRow.Children.Add(_copyFeedback);
        step3Stack.Children.Add(copyRow);

        bodyStack.Children.Add(step3Card);

        // Step 4 (Permission Approval with Screenshot)
        var step4Card = CreateStepCardHeader(
            4,
            L.T("최초 대화 시 권한 승인 창에서 [항상 허용] 클릭 (필수!)", "Click [Always Allow] on Tool Permission Prompt (First Time Only)")
        );
        var step4Stack = (StackPanel)step4Card.Child;

        var step4Desc = new TextBlock
        {
            Text = L.T("Claude 대화창에 메시지를 보내면, Claude가 상태 보고 도구를 호출하며 아래와 같은 권한 확인 창이 뜹니다.\n반드시 빨간색 상자로 표시된 [항상 허용]을 클릭해주세요!\n※ [한 번 허용]을 누르면 대화할 때마다 계속 창이 뜨게 되므로, 꼭 [항상 허용]을 눌러두셔야 매번 자동으로 Pixel Office 사무실 책상에 직원이 출근하고 실시간 말풍선이 뜹니다.",
                       "When chatting with Claude, a permission prompt will appear asking to use 'Office report status'.\nBe sure to click [Always Allow] so Claude can automatically report to your Pixel Office desk without prompting every time!"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd8)),
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        step4Stack.Children.Add(step4Desc);
        step4Stack.Children.Add(CreateGuideImage("claude_permission_guide.png"));
        bodyStack.Children.Add(step4Card);

        // Step 5 (Final: Live Office & Commands)
        bodyStack.Children.Add(CreateStepCard(
            5,
            L.T("실시간 출근 확인 및 양방향 업무 지시 (연동 완료!)", "Live Avatar Status & Bidirectional Commands (All Done!)"),
            L.T("• [실시간 출근 & 말풍선]: 이제 Claude와 대화하면 자동으로 가상 사무실 책상에 직원이 출근하고 실시간 작업 말풍선이 뜹니다. (완료 보고는 8초간 표시된 후 자동으로 닫힙니다)\n• [양방향 업무 지시]: 사무실 화면에서 캐릭터를 클릭하여 말풍선 지시를 내리거나 모바일 웹 리모컨에서 업무 지시를 입력하면, 다음 도구 호출 시 Claude가 사용자 지시를 즉시 확인하고 최우선으로 자동 수행합니다!",
                "• [Live Avatar & Speech]: When chatting with Claude, employees arrive at their desks with real-time speech bubbles.\n• [Bidirectional Instruction]: Click the employee avatar in Pixel Office or send instructions from mobile, and Claude will automatically receive and execute your command!")
        ));

        scrollViewer.Content = bodyStack;
        Grid.SetRow(scrollViewer, 1);
        rootGrid.Children.Add(scrollViewer);

        // ------------------ Footer ------------------
        var footer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3d)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(22, 12, 22, 12)
        };
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal };

        var reinstallBtn = new Button
        {
            Content = L.T("⚙️ 설정 복구 / 재등록 (평소 불필요)", "⚙️ Restore Config (Optional)"),
            ToolTip = L.T("이미 자동 등록되어 있으므로 평소에는 누르실 필요가 없습니다. 설정 파일이 손상되었을 때만 누르세요.",
                          "Already configured automatically. Only use if config file was corrupted."),
            Padding = new Thickness(14, 6, 14, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x3f, 0x3f, 0x46)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0)
        };
        reinstallBtn.Click += (_, _) => ReinstallMcp();
        leftButtons.Children.Add(reinstallBtn);

        _uninstallBtn = new Button
        {
            Content = L.T("🗑️ 연동 삭제 (해제)", "🗑️ Delete Integration"),
            ToolTip = L.T("Claude 데스크톱 앱에서 Pixel Office MCP 설정을 완전히 제거합니다.",
                          "Remove Pixel Office MCP server from Claude Desktop."),
            Padding = new Thickness(14, 6, 14, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x7f, 0x1d, 0x1d)), // Dark Red
            Foreground = new SolidColorBrush(Color.FromRgb(0xfc, 0xa5, 0xa5)), // Light red text
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x1b, 0x1b)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _uninstallBtn.Click += (_, _) => UninstallMcp();
        leftButtons.Children.Add(_uninstallBtn);

        Grid.SetColumn(leftButtons, 0);
        footerGrid.Children.Add(leftButtons);

        var closeBtn = new Button
        {
            Content = L.T("닫기", "Close"),
            Padding = new Thickness(22, 6, 22, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5b)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        closeBtn.Click += (_, _) => Close();
        Grid.SetColumn(closeBtn, 2);
        footerGrid.Children.Add(closeBtn);

        footer.Child = footerGrid;
        Grid.SetRow(footer, 2);
        rootGrid.Children.Add(footer);

        Content = rootGrid;

        RefreshStatus();
    }

    private void TogglePathView()
    {
        if (_pathDetailPanel.Visibility == Visibility.Visible)
        {
            _pathDetailPanel.Visibility = Visibility.Collapsed;
            _togglePathBtn.Content = L.T("설정 경로 보기 [클릭]", "View config paths");
        }
        else
        {
            _pathDetailPanel.Visibility = Visibility.Visible;
            _togglePathBtn.Content = L.T("설정 경로 닫기", "Hide config paths");
        }
    }

    private void RefreshStatus()
    {
        bool installed = McpSetup.IsInstalled();
        var paths = McpSetup.GetConfigPaths();
        var existingPaths = paths.Where(File.Exists).ToList();

        if (installed)
        {
            _statusBadgePill.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x53, 0x2d)); // Soft dark green
            _statusBadgeText.Text = L.T("● 자동 연동 준비 완료", "● Ready");
            _statusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x4a, 0xde, 0x80)); // Vibrant green
            _statusDescText.Text = L.T("✅ Pixel Office MCP 도구가 Claude Desktop 설정에 이미 정상 등록되어 있습니다!\n(별도 등록 버튼을 누르실 필요 없이, 아래 Step 1부터 순서대로 진행하시면 됩니다)",
                                      "✅ Pixel Office MCP tool is already automatically configured in Claude Desktop!\n(No extra setup button needed; simply follow Step 1 below.)");
            if (_uninstallBtn != null)
            {
                _uninstallBtn.IsEnabled = true;
                _uninstallBtn.Opacity = 1.0;
            }
        }
        else
        {
            _statusBadgePill.Background = new SolidColorBrush(Color.FromRgb(0x71, 0x3f, 0x12)); // Soft amber
            _statusBadgeText.Text = L.T("● MCP 미등록 (연동 해제됨)", "● Not Registered");
            _statusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xfb, 0xbf, 0x24)); // Vibrant amber
            _statusDescText.Text = L.T("Claude 데스크톱 앱에 Pixel Office MCP 설정이 등록되어 있지 않습니다.\n다시 연동하시려면 하단의 [⚙️ 설정 복구 / 재등록] 버튼을 눌러주세요.",
                                      "Pixel Office MCP tool is not registered in Claude Desktop.\nClick [⚙️ Restore Config] below to register.");
            if (_uninstallBtn != null)
            {
                _uninstallBtn.IsEnabled = false;
                _uninstallBtn.Opacity = 0.45;
            }
        }

        _pathListText.Text = existingPaths.Count > 0
            ? string.Join(Environment.NewLine, existingPaths)
            : L.T("(설정 파일 경로 없음)", "(No config files found)");
    }

    private void CopyPrompt()
    {
        bool copied = false;
        try
        {
            _promptBox.Focus();
            _promptBox.SelectAll();

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetDataObject(_promptBox.Text, true);
                    copied = true;
                    break;
                }
                catch
                {
                    Thread.Sleep(30);
                }
            }

            if (!copied)
            {
                _promptBox.Copy();
                copied = true;
            }
        }
        catch
        {
            try
            {
                _promptBox.Focus();
                _promptBox.SelectAll();
            }
            catch { }
        }

        // Visual feedback
        _copyBtn.Background = new SolidColorBrush(Color.FromRgb(0x16, 0xa3, 0x4a)); // Vibrant Green
        _copyBtn.Content = L.T("✓ 복사 완료!", "✓ Copied!");
        _copyFeedback.Text = L.T("클립보드에 복사되었습니다! 지침 창에 붙여넣기(Ctrl+V) 하세요.",
                                 "Copied to clipboard! Paste into Project Instructions (Ctrl+V).");

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _copyBtn.Background = new SolidColorBrush(Color.FromRgb(0x4f, 0x46, 0xe5));
            _copyBtn.Content = L.T("📋 지침 문구 복사하기", "📋 Copy Instructions Prompt");
        };
        timer.Start();
    }

    private void ReinstallMcp()
    {
        bool ok = McpSetup.Install();
        RefreshStatus();
        if (ok)
        {
            MessageBox.Show(this,
                L.T("Claude 데스크톱 MCP 설정이 성공적으로 등록되었습니다.\r\nClaude 데스크톱 앱을 완전히 종료 후 다시 실행해 주세요.",
                    "Claude Desktop MCP configuration registered successfully!\r\nPlease restart Claude Desktop app completely."),
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this,
                L.T("MCP 설정 등록에 실패했습니다. 관리자 권한이나 파일 권한을 확인해 주세요.",
                    "Failed to register MCP configuration."),
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UninstallMcp()
    {
        var res = MessageBox.Show(this,
            L.T("Claude 데스크톱 앱에서 Pixel Office MCP 연동 설정을 삭제(해제)하시겠습니까?\n\n※ 삭제 후 Claude 데스크톱 앱을 완전히 종료 후 다시 시작하시면 도구 연결이 해제됩니다.",
                "Do you want to remove the Pixel Office MCP integration from Claude Desktop?\n\n※ Restart Claude Desktop completely to apply changes."),
            Title, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (res != MessageBoxResult.Yes) return;

        bool ok = McpSetup.Uninstall();
        RefreshStatus();
        if (ok)
        {
            MessageBox.Show(this,
                L.T("MCP 연동 설정이 성공적으로 삭제되었습니다!\n\n[중요 뒷정리 안내]\n1. Claude 프로젝트 화면 우측의 [지침] [편집]을 눌러 연동 문구도 함께 삭제해 주세요.\n2. Claude 데스크톱 앱을 트레이 아이콘 우클릭 -> Quit으로 완전히 껐다 켜주세요.",
                    "MCP configuration removed successfully!\n\n[Important Cleanup Steps]\n1. Please also remove the prompt text from your Claude Project [Instructions].\n2. Quit Claude Desktop completely from tray and restart it."),
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this,
                L.T("삭제할 MCP 설정이 없거나 이미 삭제되어 있습니다.\n\n※ Claude 프로젝트의 [지침]에 넣으셨던 연동 문구도 [편집]을 눌러 함께 삭제해 주세요.",
                    "No MCP configuration found or already removed.\n\n※ Please also clear the prompt text from your Claude Project Instructions."),
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static FrameworkElement CreateGuideImage(string assetName, string? base64Fallback = null)
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "wwwroot", "assets", assetName);
            if (!File.Exists(path))
            {
                var candidate = Path.Combine(baseDir, "..", "..", "..", "wwwroot", "assets", assetName);
                if (File.Exists(candidate)) path = candidate;
            }

            BitmapImage? bitmap = null;
            if (File.Exists(path))
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
            }
            else if (!string.IsNullOrEmpty(base64Fallback))
            {
                var bytes = Convert.FromBase64String(base64Fallback);
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.EndInit();
                bitmap.Freeze();
            }

            if (bitmap != null)
            {
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3f, 0x3f, 0x46)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 8, 0, 10),
                    ClipToBounds = true,
                    Child = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };
                return border;
            }
        }
        catch { }
        return new FrameworkElement { Height = 0 };
    }

    private static Border CreateStepCardHeader(int step, string title)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2a)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3f, 0x3f, 0x46)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var stack = new StackPanel();

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var stepBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x3b, 0x82, 0xf6)), // Blue
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 8, 0)
        };
        stepBadge.Child = new TextBlock
        {
            Text = $"Step {step}",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        };
        titleRow.Children.Add(stepBadge);

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(titleText);
        stack.Children.Add(titleRow);

        border.Child = stack;
        return border;
    }

    private static Border CreateStepCard(int step, string title, string description)
    {
        var card = CreateStepCardHeader(step, title);
        var stack = (StackPanel)card.Child;

        var descText = new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd8)),
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(descText);

        return card;
    }
}
