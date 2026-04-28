using System.Text;

namespace KeyMouseSyncReplica;

public static class WindowPicker
{
    public static WindowInfo? PickWindowUnderCursor(IntPtr ownerHandle, out string error)
    {
        error = string.Empty;

        if (!NativeMethods.GetCursorPos(out var point))
        {
            error = "无法获取当前鼠标位置。";
            return null;
        }

        return PickWindowAtPoint(ownerHandle, new Point(point.X, point.Y), out error);
    }

    public static WindowInfo? PickWindowAtPoint(IntPtr ownerHandle, Point screenPoint, out string error)
    {
        error = string.Empty;

        var point = new NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y };
        var hwnd = PickBestWindowAtPoint(point);
        if (hwnd == IntPtr.Zero)
        {
            error = "鼠标下没有可用窗口。";
            return null;
        }

        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            error = "鼠标下窗口句柄无效。";
            return null;
        }

        if (hwnd == ownerHandle || NativeMethods.IsChild(ownerHandle, hwnd))
        {
            error = "不能选择复刻器自身窗口。";
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        var title = GetTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title) && root != IntPtr.Zero)
        {
            title = GetTitle(root);
        }

        return new WindowInfo(hwnd, unchecked((int)processId), title);
    }

    public static string GetTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static IntPtr PickBestWindowAtPoint(NativeMethods.POINT point)
    {
        var hwnd = NativeMethods.WindowFromPoint(point);
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var bestRect))
        {
            return hwnd;
        }

        var parent = NativeMethods.GetParent(hwnd);
        if (parent == IntPtr.Zero)
        {
            return hwnd;
        }

        for (var candidate = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT);
             candidate != IntPtr.Zero;
             candidate = NativeMethods.GetWindow(candidate, NativeMethods.GW_HWNDNEXT))
        {
            if (NativeMethods.GetParent(candidate) != parent ||
                !NativeMethods.IsWindowVisible(candidate) ||
                !NativeMethods.GetWindowRect(candidate, out var candidateRect) ||
                !Contains(candidateRect, point))
            {
                continue;
            }

            if (Area(candidateRect) < Area(bestRect))
            {
                bestRect = candidateRect;
                hwnd = candidate;
            }
        }

        return hwnd;
    }

    private static bool Contains(NativeMethods.RECT rect, NativeMethods.POINT point)
    {
        return point.X >= rect.Left &&
            point.X < rect.Right &&
            point.Y >= rect.Top &&
            point.Y < rect.Bottom;
    }

    private static long Area(NativeMethods.RECT rect)
    {
        return Math.Max(0, rect.Right - rect.Left) * (long)Math.Max(0, rect.Bottom - rect.Top);
    }
}
