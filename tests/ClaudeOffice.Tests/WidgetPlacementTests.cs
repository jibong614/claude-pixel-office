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
}
