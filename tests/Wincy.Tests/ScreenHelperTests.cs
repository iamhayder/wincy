using Wincy.Interop;
using Xunit;

namespace Wincy.Tests;

public class ScreenHelperTests
{
    // A 1920x1080 screen with a 40px taskbar along the bottom.
    private static readonly MonitorInfo Screen = new(
        IntPtr.Zero,
        new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
        new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
        "DISPLAY1",
        true,
        1.0);

    [Fact]
    public void LeavesAWindowThatAlreadyFitsAlone()
    {
        var (x, y) = ScreenHelper.Constrain(400, 300, 450, 600, Screen);

        Assert.Equal(400, x);
        Assert.Equal(300, y);
    }

    [Fact]
    public void PullsBackFromTheRightEdge()
    {
        // This is what happens when opening the preview widens the popup: it must
        // slide left rather than hang off the screen.
        var (x, _) = ScreenHelper.Constrain(1700, 100, 850, 600, Screen);

        Assert.Equal(1920 - 850, x);
    }

    [Fact]
    public void PullsBackAboveTheTaskbar()
    {
        var (_, y) = ScreenHelper.Constrain(100, 900, 450, 600, Screen);

        Assert.Equal(1040 - 600, y);
    }

    [Fact]
    public void ClampsNegativeCoordinatesToTheWorkArea()
    {
        var (x, y) = ScreenHelper.Constrain(-200, -50, 450, 600, Screen);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void AWindowTallerThanTheScreenStartsAtTheTop()
    {
        // Math.Max in Constrain keeps this from producing a negative origin.
        var (_, y) = ScreenHelper.Constrain(0, 500, 450, 2000, Screen);

        Assert.Equal(0, y);
    }

    [Fact]
    public void AWindowWiderThanTheScreenStartsAtTheLeft()
    {
        var (x, _) = ScreenHelper.Constrain(500, 0, 3000, 600, Screen);

        Assert.Equal(0, x);
    }

    [Fact]
    public void RespectsAMonitorThatDoesNotStartAtTheOrigin()
    {
        // A second screen placed to the left of the primary.
        var left = new MonitorInfo(
            IntPtr.Zero,
            new RECT { Left = -1920, Top = 0, Right = 0, Bottom = 1080 },
            new RECT { Left = -1920, Top = 0, Right = 0, Bottom = 1040 },
            "DISPLAY2",
            false,
            1.0);

        var (x, y) = ScreenHelper.Constrain(-2500, 900, 450, 600, left);

        Assert.Equal(-1920, x);
        Assert.Equal(1040 - 600, y);
    }

    [Fact]
    public void RectReportsItsOwnWidthAndHeight()
    {
        var rect = new RECT { Left = 10, Top = 20, Right = 110, Bottom = 220 };

        Assert.Equal(100, rect.Width);
        Assert.Equal(200, rect.Height);
    }
}
