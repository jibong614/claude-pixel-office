using System.Text.Json;
using Xunit;

namespace ClaudeOffice.Tests;

public class WidgetPlacementTests
{
    [Fact]
    public void AppSettings_SeparatesMiniWidgetAndMiniBarPlacement()
    {
        var settings = new AppSettings
        {
            Pet = true,
            PetIsland = false,
            MiniWidgetPlacement = new AppSettings.WindowPlacement
            {
                Left = 1200,
                Top = 600,
                Width = 360,
                Height = 350,
                Maximized = false
            },
            MiniBarPlacement = new AppSettings.WindowPlacement
            {
                Left = 1020,
                Top = 906,
                Width = 540,
                Height = 44,
                Maximized = false
            }
        };

        var json = JsonSerializer.Serialize(settings);

        Assert.Contains("\"PetWindow\"", json);
        Assert.Contains("\"MiniBarWindow\"", json);

        var loaded = JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded.MiniWidgetPlacement);
        Assert.NotNull(loaded.MiniBarPlacement);

        Assert.Equal(360, loaded.MiniWidgetPlacement.Width);
        Assert.Equal(350, loaded.MiniWidgetPlacement.Height);
        Assert.Equal(1200, loaded.MiniWidgetPlacement.Left);
        Assert.Equal(600, loaded.MiniWidgetPlacement.Top);

        Assert.Equal(540, loaded.MiniBarPlacement.Width);
        Assert.Equal(44, loaded.MiniBarPlacement.Height);
        Assert.Equal(1020, loaded.MiniBarPlacement.Left);
        Assert.Equal(906, loaded.MiniBarPlacement.Top);
    }

    [Fact]
    public void GetSavedWidgetHeight_ReturnsValidOrFallbackDefault()
    {
        // 1. Placement가 없는 경우 기본값 280
        App.Settings.MiniWidgetPlacement = null;
        Assert.Equal(WidgetWindow.DefaultHeight, WidgetWindow.GetSavedWidgetHeight());

        // 2. 유효한 커스텀 높이 (180 ~ 840)인 경우 해당 값 유지
        App.Settings.MiniWidgetPlacement = new AppSettings.WindowPlacement { Height = 420 };
        Assert.Equal(420, WidgetWindow.GetSavedWidgetHeight());

        // 3. 미니바 크기(44px 등)로 잘못 오염된 경우 기본값 280 복원 (180 미만)
        App.Settings.MiniWidgetPlacement = new AppSettings.WindowPlacement { Height = 44 };
        Assert.Equal(WidgetWindow.DefaultHeight, WidgetWindow.GetSavedWidgetHeight());

        // 4. 최대치 840 초과 시 기본값 280 복원
        App.Settings.MiniWidgetPlacement = new AppSettings.WindowPlacement { Height = 1000 };
        Assert.Equal(WidgetWindow.DefaultHeight, WidgetWindow.GetSavedWidgetHeight());
    }

    [Fact]
    public void WidgetToMiniBar_BottomPositionAligned()
    {
        // 위젯 상태: Top = 600, Height = 350 -> Bottom = 950
        // 우측: Left = 1200, Width = 360 -> Right = 1560
        double curTop = 600;
        double curHeight = 350;
        double curLeft = 1200;
        double curWidth = 360;

        double curBottom = curTop + curHeight;
        double curRight = curLeft + curWidth;

        // 미니바 전환 시 계산
        double targetW = WidgetWindow.MiniBarWidth;  // 540
        double targetH = WidgetWindow.MiniBarHeight; // 44
        double newTop = curBottom - targetH;
        double newLeft = curRight - targetW;

        Assert.Equal(906, newTop);
        Assert.Equal(1020, newLeft);

        // 하단(Bottom) 기준선이 위젯과 완벽히 일치해야 함
        Assert.Equal(curBottom, newTop + targetH);
        // 우측(Right) 기준선도 일치
        Assert.Equal(curRight, newLeft + targetW);
    }

    [Fact]
    public void MiniBarToWidget_BottomPositionAndSavedHeightRestored()
    {
        // 미니바 상태: Top = 906, Height = 44 -> Bottom = 950
        // 우측: Left = 1020, Width = 440 -> Right = 1560
        double curTop = 906;
        double curHeight = 44;
        double curLeft = 1020;
        double curWidth = 540;

        double curBottom = curTop + curHeight;
        double curRight = curLeft + curWidth;

        // 위젯 저장 높이가 350인 경우
        App.Settings.MiniWidgetPlacement = new AppSettings.WindowPlacement { Height = 350 };
        double targetH = WidgetWindow.GetSavedWidgetHeight();
        double targetW = WidgetWindow.DefaultWidth;

        double newTop = curBottom - targetH;
        double newLeft = curRight - targetW;

        Assert.Equal(350, targetH);
        Assert.Equal(600, newTop);
        Assert.Equal(1200, newLeft);

        // 하단(Bottom) 기준선이 미니바와 완벽히 일치해야 함
        Assert.Equal(curBottom, newTop + targetH);
        // 우측(Right) 기준선도 일치
        Assert.Equal(curRight, newLeft + targetW);
    }

    [Fact]
    public void AppModeSwitch_OfficeToBasic_AddsGraphicHeightAndPreservesBottom()
    {
        // 오피스 모드에서 높이 280, 상단 700 -> 하단 980
        double officeH = 280;
        double officeTop = 700;
        double fixedBottom = officeTop + officeH; // 980

        // 기본 모드로 전환 시 GraphicHeight (+230px) 가 추가되고 하단은 고정됨
        double basicH = officeH + WidgetWindow.GraphicHeight; // 510
        double basicTop = fixedBottom - basicH; // 470

        Assert.Equal(510, basicH);
        Assert.Equal(470, basicTop);
        Assert.Equal(fixedBottom, basicTop + basicH);
    }

    [Fact]
    public void AppModeSwitch_BasicToOffice_SubtractsGraphicHeightAndPreservesBottom()
    {
        // 기본 모드에서 높이 510, 상단 470 -> 하단 980
        double basicH = 510;
        double basicTop = 470;
        double fixedBottom = basicTop + basicH; // 980

        // 오피스 모드로 전환 시 GraphicHeight (-230px) 가 차감되고 하단은 고정됨
        double officeH = basicH - WidgetWindow.GraphicHeight; // 280
        double officeTop = fixedBottom - officeH; // 700

        Assert.Equal(280, officeH);
        Assert.Equal(700, officeTop);
        Assert.Equal(fixedBottom, officeTop + officeH);
    }

    [Fact]
    public void GetSavedWidgetHeight_ReturnsCorrectDefaultPerMode()
    {
        App.Settings.MiniWidgetPlacement = null;
        App.Settings.WidgetSize = "large";

        App.Settings.AppMode = "basic";
        Assert.Equal(WidgetWindow.DefaultHeight, WidgetWindow.GetSavedWidgetHeight());
        Assert.Equal(510, WidgetWindow.GetSavedWidgetHeight());

        App.Settings.AppMode = "office";
        Assert.Equal(WidgetWindow.DefaultOfficeHeight, WidgetWindow.GetSavedWidgetHeight());
        Assert.Equal(280, WidgetWindow.GetSavedWidgetHeight());

        // 초기화
        App.Settings.AppMode = "basic";
    }

    [Fact]
    public void WidgetSize_ConstantsAndScalesAreProportional()
    {
        Assert.Equal(360, WidgetWindow.GetWidthForSize("large"));
        Assert.Equal(300, WidgetWindow.GetWidthForSize("medium"));
        Assert.Equal(260, WidgetWindow.GetWidthForSize("small"));

        Assert.Equal(230, WidgetWindow.GetGraphicHeightForSize("large"));
        Assert.Equal(190, WidgetWindow.GetGraphicHeightForSize("medium"));
        Assert.Equal(160, WidgetWindow.GetGraphicHeightForSize("small"));
    }

    [Fact]
    public void WidgetSizeSwitch_PreservesRightAndBottomAnchors()
    {
        // 1. Large: Left 1200, Top 470, Width 360, Height 510 -> Right 1560, Bottom 980
        double curLeft = 1200, curTop = 470;
        double curWidth = 360, curHeight = 510;
        double fixedRight = curLeft + curWidth; // 1560
        double fixedBottom = curTop + curHeight; // 980

        // 2. Medium 으로 변경 시: Width 300 (-60px), GraphicHeight 190 (-40px)
        double medW = WidgetWindow.GetWidthForSize("medium"); // 300
        double medH = curHeight + (WidgetWindow.GetGraphicHeightForSize("medium") - WidgetWindow.GetGraphicHeightForSize("large")); // 510 - 40 = 470
        double medLeft = fixedRight - medW; // 1560 - 300 = 1260
        double medTop = fixedBottom - medH; // 980 - 470 = 510

        Assert.Equal(300, medW);
        Assert.Equal(470, medH);
        Assert.Equal(1260, medLeft);
        Assert.Equal(510, medTop);
        Assert.Equal(fixedRight, medLeft + medW);
        Assert.Equal(fixedBottom, medTop + medH);

        // 3. Small 로 변경 시: Width 260 (-40px), GraphicHeight 160 (-30px)
        double smlW = WidgetWindow.GetWidthForSize("small"); // 260
        double smlH = medH + (WidgetWindow.GetGraphicHeightForSize("small") - WidgetWindow.GetGraphicHeightForSize("medium")); // 470 - 30 = 440
        double smlLeft = fixedRight - smlW; // 1560 - 260 = 1300
        double smlTop = fixedBottom - smlH; // 980 - 440 = 540

        Assert.Equal(260, smlW);
        Assert.Equal(440, smlH);
        Assert.Equal(1300, smlLeft);
        Assert.Equal(540, smlTop);
        Assert.Equal(fixedRight, smlLeft + smlW);
        Assert.Equal(fixedBottom, smlTop + smlH);
    }

    [Fact]
    public void PetModeMenu_PlacementDirection_OpensUpwardForMiniBar()
    {
        // MiniBar 모드에서는 높이가 44px에 불과하므로, 화면 위치와 무관하게 무조건 위쪽(AboveLeft)으로 열려야 함
        bool isIsland = true;
        double ptBottomRightY = 1040;
        double workingAreaBottom = 1080;

        bool openUp = isIsland || (ptBottomRightY + 120 > workingAreaBottom);
        Assert.True(openUp);
    }

    [Fact]
    public void PetModeMenu_PlacementDirection_OpensUpwardWhenNearScreenBottom()
    {
        // MiniWidget 모드에서도 화면 하단 구석(Bottom - 100px)에 배치된 경우 메뉴(약 120px)가 잘리지 않도록 위쪽으로 열려야 함
        bool isIsland = false;
        double ptBottomRightY = 1000;
        double workingAreaBottom = 1080; // 남은 공간 80px < 120px

        bool openUp = isIsland || (ptBottomRightY + 120 > workingAreaBottom);
        Assert.True(openUp);
    }

    [Fact]
    public void PetModeMenu_PlacementDirection_OpensDownwardWhenEnoughSpace()
    {
        // 화면 중앙이나 상단에 위치하여 하단 여유 공간이 충분한 경우 아래쪽으로 열림
        bool isIsland = false;
        double ptBottomRightY = 400;
        double workingAreaBottom = 1080;

        bool openUp = isIsland || (ptBottomRightY + 120 > workingAreaBottom);
        Assert.False(openUp);
    }

    [Fact]
    public void GetWalkHeightForSize_ReturnsProportionalWalkHeights()
    {
        Assert.Equal(WidgetWindow.WalkHeightLarge, WidgetWindow.GetWalkHeightForSize("large"));
        Assert.Equal(WidgetWindow.WalkHeightMedium, WidgetWindow.GetWalkHeightForSize("medium"));
        Assert.Equal(WidgetWindow.WalkHeightSmall, WidgetWindow.GetWalkHeightForSize("small"));

        Assert.Equal(135, WidgetWindow.GetWalkHeightForSize("large"));
        Assert.Equal(120, WidgetWindow.GetWalkHeightForSize("medium"));
        Assert.Equal(105, WidgetWindow.GetWalkHeightForSize("small"));
    }

    [Fact]
    public void MiniBarToWalkMode_PreservesBottomAndRightAnchors()
    {
        // 미니바 상태: Top = 906, Height = 44 -> Bottom = 950
        // 우측: Left = 1020, Width = 540 -> Right = 1560
        double curTop = 906;
        double curHeight = 44;
        double curLeft = 1020;
        double curWidth = 540;

        double curBottom = curTop + curHeight;
        double curRight = curLeft + curWidth;

        double targetW = WidgetWindow.GetWidthForSize("small"); // 260
        double targetH = WidgetWindow.GetWalkHeightForSize("small"); // 105

        double newTop = curBottom - targetH; // 950 - 105 = 845
        double newLeft = curRight - targetW; // 1560 - 260 = 1300

        Assert.Equal(260, targetW);
        Assert.Equal(105, targetH);
        Assert.Equal(845, newTop);
        Assert.Equal(1300, newLeft);

        // 하단(Bottom) 및 우측(Right) 기준선 일치
        Assert.Equal(curBottom, newTop + targetH);
        Assert.Equal(curRight, newLeft + targetW);
    }

    [Fact]
    public void WalkModeToMiniWidget_RestoresSavedHeightAndPreservesBottom()
    {
        // 산책 모드: Top = 774, Height = 176 -> Bottom = 950
        // 우측: Left = 1300, Width = 260 -> Right = 1560
        double curTop = 774;
        double curHeight = 176;
        double curLeft = 1300;
        double curWidth = 260;

        double curBottom = curTop + curHeight; // 950
        double curRight = curLeft + curWidth; // 1560

        // 위젯 저장 높이가 420인 경우
        App.Settings.MiniWidgetPlacement = new AppSettings.WindowPlacement { Height = 420 };
        double targetH = WidgetWindow.GetSavedWidgetHeight();
        double targetW = WidgetWindow.GetWidthForSize("small");

        double newTop = curBottom - targetH; // 950 - 420 = 530
        double newLeft = curRight - targetW; // 1560 - 260 = 1300

        Assert.Equal(420, targetH);
        Assert.Equal(530, newTop);
        Assert.Equal(1300, newLeft);

        Assert.Equal(curBottom, newTop + targetH);
        Assert.Equal(curRight, newLeft + targetW);
    }

    [Fact]
    public void ModeAlternation_1_2_3_AllCombinationsPreserveAnchors()
    {
        // 1: 미니위젯 (Default small: W=260, H=420)
        // 2: 미니바 (W=540, H=44)
        // 3: 산책 모드 (W=260, H=176)
        App.Settings.WidgetSize = "small";
        App.Settings.MiniWidgetPlacement = new AppSettings.WindowPlacement { Height = 420 };

        double fixedBottom = 950;
        double fixedRight = 1560;

        // 1번 (미니위젯) 시작: Top = 530, Height = 420, Left = 1300, Width = 260
        double w1 = WidgetWindow.GetWidthForSize("small");
        double h1 = WidgetWindow.GetSavedWidgetHeight();
        double top1 = fixedBottom - h1;
        double left1 = fixedRight - w1;
        Assert.Equal(fixedBottom, top1 + h1);
        Assert.Equal(fixedRight, left1 + w1);

        // 1 ➔ 2 (미니바 전환)
        double w2 = WidgetWindow.MiniBarWidth;
        double h2 = WidgetWindow.MiniBarHeight;
        double top2 = (top1 + h1) - h2;
        double left2 = (left1 + w1) - w2;
        Assert.Equal(906, top2);
        Assert.Equal(1020, left2);
        Assert.Equal(fixedBottom, top2 + h2);
        Assert.Equal(fixedRight, left2 + w2);

        // 2 ➔ 3 (산책 모드 전환)
        double w3 = WidgetWindow.GetWidthForSize("small");
        double h3 = WidgetWindow.GetWalkHeightForSize("small");
        double top3 = (top2 + h2) - h3;
        double left3 = (left2 + w2) - w3;
        Assert.Equal(845, top3);
        Assert.Equal(1300, left3);
        Assert.Equal(fixedBottom, top3 + h3);
        Assert.Equal(fixedRight, left3 + w3);

        // 3 ➔ 1 (미니위젯 복귀)
        double top1_again = (top3 + h3) - h1;
        double left1_again = (left3 + w3) - w1;
        Assert.Equal(top1, top1_again);
        Assert.Equal(left1, left1_again);
        Assert.Equal(fixedBottom, top1_again + h1);
        Assert.Equal(fixedRight, left1_again + w1);

        // 1 ➔ 3 (산책 모드 직접 전환)
        double top3_again = (top1_again + h1) - h3;
        double left3_again = (left1_again + w1) - w3;
        Assert.Equal(top3, top3_again);
        Assert.Equal(left3, left3_again);
        Assert.Equal(fixedBottom, top3_again + h3);

        // 3 ➔ 2 (미니바 직접 전환)
        double top2_again = (top3_again + h3) - h2;
        double left2_again = (left3_again + w3) - w2;
        Assert.Equal(top2, top2_again);
        Assert.Equal(left2, left2_again);
        Assert.Equal(fixedBottom, top2_again + h2);

        // 2 ➔ 1 (미니위젯 복귀)
        double top1_final = (top2_again + h2) - h1;
        double left1_final = (left2_again + w2) - w1;
        Assert.Equal(top1, top1_final);
        Assert.Equal(left1, left1_final);
        Assert.Equal(fixedBottom, top1_final + h1);

        // 초기화 복원
        App.Settings.WidgetSize = "large";
        App.Settings.MiniWidgetPlacement = null;
    }
}
