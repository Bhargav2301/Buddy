using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Graphics.Imaging;

namespace Buddy.Windows;

internal sealed record CapturedWindow(byte[] Image, System.Windows.Rect Bounds, int PixelWidth, int PixelHeight) : IDisposable
{
    internal List<OcrText> Text { get; init; } = [];
    internal CapturedWindow Mask(OcrObservation observation)
    {
        using var input = new MemoryStream(Image); using var bitmap = new System.Drawing.Bitmap(input);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) foreach (var mask in observation.PrivateBounds) {
            float x = (float)((mask.X - Bounds.X) * PixelWidth / Bounds.Width), y = (float)((mask.Y - Bounds.Y) * PixelHeight / Bounds.Height);
            float width = (float)(mask.Width * PixelWidth / Bounds.Width), height = (float)(mask.Height * PixelHeight / Bounds.Height);
            graphics.FillRectangle(System.Drawing.Brushes.Black, x - 2, y - 2, width + 4, height + 4);
        }
        using var output = new MemoryStream(); bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return new(output.ToArray(), Bounds, PixelWidth, PixelHeight) { Text = observation.Text };
    }
    internal byte[] ForVision()
    {
        using var input = new MemoryStream(Image); using var full = System.Drawing.Image.FromStream(input);
        double scale = Math.Min(1, 1280d / Math.Max(full.Width, full.Height));
        using var resized = new System.Drawing.Bitmap(full, Math.Max(1, (int)(full.Width * scale)), Math.Max(1, (int)(full.Height * scale)));
        using var output = new MemoryStream(); resized.Save(output, System.Drawing.Imaging.ImageFormat.Jpeg); return output.ToArray();
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(Image);
}

internal static class WindowCapture
{
    // HWND interop described in Microsoft's WPF Windows.Graphics.Capture sample.
    // Every session retains the OS capture border and owns its device/frame pool.
    internal static async Task<CapturedWindow> Capture(IntPtr window, IReadOnlyList<System.Windows.Rect> masks, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!GraphicsCaptureSession.IsSupported()) throw new InvalidOperationException("Windows Graphics Capture is unavailable on this PC.");
        Native.CheckWindow(window);
        var bounds = Bounds(window);
        if (Native.GetForegroundWindow() != window) throw new InvalidOperationException("Activate the target window before capturing it.");
        var item = CreateItem(window);
        if (item.Size.Width < 1 || item.Size.Height < 1 || (long)item.Size.Width * item.Size.Height > 16_000_000)
            throw new InvalidOperationException("Restore the window at a smaller size before capturing it.");
        if (Math.Abs(bounds.Width - item.Size.Width) > 1 || Math.Abs(bounds.Height - item.Size.Height) > 1)
            throw new InvalidOperationException("Windows could not establish the capture coordinates. Restore the window and retry.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var device = CreateDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        timeout.Token.ThrowIfCancellationRequested();
        session.StartCapture();
        while (true) {
            timeout.Token.ThrowIfCancellationRequested();
            using var frame = pool.TryGetNextFrame();
            if (frame is null) { await Task.Delay(20, timeout.Token); continue; }
            if (frame.ContentSize.Width != item.Size.Width || frame.ContentSize.Height != item.Size.Height || Bounds(window) != bounds || Native.GetForegroundWindow() != window)
                throw new InvalidOperationException("The window moved, resized or lost focus. Capture again.");
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied).AsTask(timeout.Token);
            int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
            var pixels = new byte[checked(width * height * 4)];
            try {
                bitmap.CopyToBuffer(pixels.AsBuffer());
                foreach (var mask in masks) {
                    int left = Math.Clamp((int)Math.Floor(mask.Left - bounds.Left) - 2, 0, width), right = Math.Clamp((int)Math.Ceiling(mask.Right - bounds.Left) + 2, 0, width);
                    int top = Math.Clamp((int)Math.Floor(mask.Top - bounds.Top) - 2, 0, height), bottom = Math.Clamp((int)Math.Ceiling(mask.Bottom - bounds.Top) + 2, 0, height);
                    for (int y = top; y < bottom; y++) for (int x = left; x < right; x++) { int offset = (y * width + x) * 4; pixels[offset] = pixels[offset+1] = pixels[offset+2] = 0; pixels[offset+3] = 255; }
                }
                timeout.Token.ThrowIfCancellationRequested(); Native.CheckWindow(window);
                if (Bounds(window) != bounds || Native.GetForegroundWindow() != window) throw new InvalidOperationException("The capture is no longer current.");
                using var drawing = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                var locked = drawing.LockBits(new(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, drawing.PixelFormat);
                try { for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, locked.Scan0 + y * locked.Stride, width * 4); }
                finally { drawing.UnlockBits(locked); }
                using var encoded = new MemoryStream(); drawing.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
                return new(encoded.ToArray(), bounds, width, height);
            } finally { CryptographicOperations.ZeroMemory(pixels); }
        }
    }

    internal static System.Windows.Rect Bounds(IntPtr window)
    {
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(window, 9, out var rect, Marshal.SizeOf<WindowRect>()));
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) throw new InvalidOperationException("The window has no visible capture area.");
        return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    private static GraphicsCaptureItem CreateItem(IntPtr window)
    {
        IntPtr name = IntPtr.Zero, factory = IntPtr.Zero, item = IntPtr.Zero;
        try {
            const string type = "Windows.Graphics.Capture.GraphicsCaptureItem";
            Marshal.ThrowExceptionForHR(WindowsCreateString(type, type.Length, out name));
            var iid = typeof(ICaptureItemInterop).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref iid, out factory));
            var interop = (ICaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            try { var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"); item = interop.CreateForWindow(window, ref itemIid); }
            finally { Marshal.ReleaseComObject(interop); }
            return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        } finally { if (item != IntPtr.Zero) Marshal.Release(item); if (factory != IntPtr.Zero) Marshal.Release(factory); if (name != IntPtr.Zero) WindowsDeleteString(name); }
    }
    private static IDirect3DDevice CreateDevice()
    {
        IntPtr device = IntPtr.Zero, context = IntPtr.Zero, dxgi = IntPtr.Zero, projected = IntPtr.Zero;
        try {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out _, out context));
            var iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, ref iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out projected));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(projected);
        } finally { foreach (var pointer in new[] { projected, dxgi, context, device }) if (pointer != IntPtr.Zero) Marshal.Release(pointer); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICaptureItemInterop { IntPtr CreateForWindow(IntPtr window, ref Guid iid); IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid); }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out WindowRect value, int size);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, int length, out IntPtr value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels, uint levelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi, out IntPtr device);
}
