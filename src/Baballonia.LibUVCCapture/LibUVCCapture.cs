using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Runtime.InteropServices;
using System.Text;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.LibUVCCapture;

public sealed class LibUVCCapture(string source, ILogger logger) : Capture(source, logger), IDisposable
{
    static Uvc.Context uvc = new();
    static Uvc.DeviceList deviceList = default;

    Uvc.Device? _device = FindCamera(source)?.Ref();
    Uvc.DeviceHandle? _handle = default;
    GCHandle _handleDelegate = default;

    const string prefix = "USB:";
    static Uvc.Device? FindCamera(string source) {
        if(!source.StartsWith(prefix))
            return null;
        Span<byte> deviceNameBuffer = stackalloc byte[64];
        if(!Encoding.UTF8.TryGetBytes(((ReadOnlySpan<char>)source).Slice(prefix.Length), deviceNameBuffer, out int deviceNameLength))
            return null;
        ReadOnlySpan<byte> deviceName = deviceNameBuffer.Slice(0, deviceNameLength);
        foreach(Uvc.Device device in deviceList) {
            using Uvc.Device.Descriptor descriptor = device.GetDescriptor();
            if(descriptor.product.SequenceEqual(deviceName))
                return device;
        }
        return null;
    }

    public static void UpdateCameras() {
        deviceList.Dispose();
        deviceList = uvc.GetDeviceList();
    }

    public override bool CanConnect(string connectionString) {
        return false; // TODO
    }

    public override Task<bool> StartCapture() {
        Uvc.DeviceHandle handle = default;
        try {
            if(this._device is not Uvc.Device device)
                return Task.FromResult(false);
            if(this._handle != null)
                return Task.FromResult(true);
            handle = device.Open();
            // TODO: Uvc.FrameDesc format = uvc_get_format_descs().SelectMany(format => format.frameDescs).Find(/*resolution & framerate*/)
            Uvc.StreamCtrl control = handle.GetStreamCtrlFormatSize(Uvc.FrameFormat.MJPEG, format.wWidth, format.wHeight, 10000000 / (int)format.dwDefaultFrameInterval);
            this._handleDelegate = handle.StartStreaming(in control, this.OnFrame);
            this._handle = handle;
            handle = default;
            return Task.FromResult(true);
        } catch(Exception ex) {
            handle.Dispose();
            this.Logger.LogError(ex, "Failed to start capture");
            return Task.FromResult(false);
        }
    }

    private void OnFrame(in Uvc.Frame frame) {
        Mat newFrame = Mat.FromImageData(frame.data);
        if(newFrame.Width > 0 && newFrame.Height > 0)
            SetRawMat(newFrame);
        else
            newFrame.Dispose();
    }

    public override Task<bool> StopCapture() {
        if(this._handle is Uvc.DeviceHandle handle) using(handle) {
            this._handle = null;
            handle.StopStreaming(this._handleDelegate);
            this._handleDelegate = default;
        }
        return Task.FromResult(true);
    }

    public void Dispose() {
        if(this._device is not Uvc.Device device)
            return;
        this._device = null;
        device.Dispose();
    }

    ~LibUVCCapture() =>
        Dispose();
}
