using System.Runtime.InteropServices;

namespace Baballonia.LibUVCCapture.Uvc;

[StructLayout(LayoutKind.Sequential)]
partial struct Error { // enum uvc_error
    int _native;

    [return: MarshalAs(UnmanagedType.LPStr)]
    [LibraryImport("uvc")] private static partial string uvc_strerror(Error err);

    public void ThrowIfError() {
        if(this._native < 0)
            throw new Exception(uvc_strerror(this));
    }
}

enum FrameFormat : int {
    Unknown = 0,
    Any = 0,
    Uncompressed,
    Compressed,
    YUYV,
    UYVY,
    RGB,
    BGR,
    MJPEG,
    H264,
    Gray8,
    Gray16,
    BY8,
    BA81,
    SGRBG8,
    SGBRG8,
    SRGGB8,
    SBGGR8,
    NV12,
    P010,
}

enum VsDescSubtype : int {
    UVC_VS_UNDEFINED,
    UVC_VS_INPUT_HEADER,
    UVC_VS_OUTPUT_HEADER,
    UVC_VS_STILL_IMAGE_FRAME,
    UVC_VS_FORMAT_UNCOMPRESSED,
    UVC_VS_FRAME_UNCOMPRESSED,
    UVC_VS_FORMAT_MJPEG,
    UVC_VS_FRAME_MJPEG,
    UVC_VS_FORMAT_MPEG2TS = 0x0a,
    UVC_VS_FORMAT_DV = 0x0c,
    UVC_VS_COLORFORMAT,
    UVC_VS_FORMAT_FRAME_BASED = 0x10,
    UVC_VS_FRAME_FRAME_BASED,
    UVC_VS_FORMAT_STREAM_BASED,
};

[StructLayout(LayoutKind.Sequential)]
struct Frame { // struct uvc_frame
    unsafe byte* _data;
    nuint _dataBytes;
    public uint width;
    public uint height;
    public FrameFormat frameFormat;
    public nuint step;
    public uint sequence;

    // TODO: ABI for this
    /*struct timeval capture_time;
    struct timespec capture_time_finished;
    DeviceHandle source;
    byte library_owns_data;
    System.IntPtr metadata;
    nuint metadata_bytes;*/

    public unsafe Span<byte> data => new(this._data, (int)this._dataBytes);
}

delegate void FrameCallback(in Frame frame);

[StructLayout(LayoutKind.Sequential)]
struct StreamCtrl { // struct uvc_stream_ctrl
    public ushort bmHint;
    public byte bFormatIndex;
    public byte bFrameIndex;
    public uint dwFrameInterval;
    public ushort wKeyFrameRate;
    public ushort wPFrameRate;
    public ushort wCompQuality;
    public ushort wCompWindowSize;
    public ushort wDelay;
    public uint dwMaxVideoFrameSize;
    public uint dwMaxPayloadTransferSize;
    public uint dwClockFrequency;
    public byte bmFramingInfo;
    public byte bPreferredVersion;
    public byte bMinVersion;
    public byte bMaxVersion;
    public byte bInterfaceNumber;
}

[StructLayout(LayoutKind.Sequential)]
ref struct FormatDesc { // struct uvc_format_desc
    System.IntPtr _parent; // struct uvc_streaming_interface
    unsafe FormatDesc* _prev, _next;
    public VsDescSubtype bDescriptorSubtype;
    public byte bFormatIndex;
    public byte bNumFrameDescriptors;
    public unsafe fixed byte format[16]; // guidFormat[16], fourccFormat[4]
    public byte flagsOrBPP; // bBitsPerPixel, bmFlags
    public byte bDefaultFrameIndex;
    public byte bAspectRatioX;
    public byte bAspectRatioY;
    public byte bmInterlaceFlags;
    public byte bCopyProtect;
    public byte bVariableSize;
    unsafe FrameDesc* _frameDescs;
    // StillFrameDesc *_stillFrameDesc; // TODO
}

[StructLayout(LayoutKind.Sequential)]
struct FrameDesc { // struct uvc_frame_desc
    unsafe FormatDesc* _parent;
    unsafe FrameDesc* _prev, _next;
    public VsDescSubtype bDescriptorSubtype;
    public byte bFrameIndex;
    public byte bmCapabilities;
    public ushort wWidth;
    public ushort wHeight;
    public uint dwMinBitRate;
    public uint dwMaxBitRate;
    public uint dwMaxVideoFrameBufferSize;
    public uint dwDefaultFrameInterval;
    public uint dwMinFrameInterval;
    public uint dwMaxFrameInterval;
    public uint dwFrameIntervalStep;
    public byte bFrameIntervalType;
    public uint dwBytesPerLine;
    unsafe uint* _intervals;

    public unsafe Span<uint> intervals {
        get {
            int length = 0;
            if(this._intervals == null)
                return default;
            while(this._intervals[length] != 0)
                ++length;
            return new(this._intervals, length);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
partial struct DeviceHandle : IDisposable {
    IntPtr _native; // struct uvc_device_handle*

    [LibraryImport("uvc")] private static partial Error uvc_get_stream_ctrl_format_size(DeviceHandle devh, out StreamCtrl ctrl, FrameFormat format, int width, int height, int fps);
    [LibraryImport("uvc")] private static unsafe partial Error uvc_start_streaming(DeviceHandle devh, ref StreamCtrl ctrl, delegate* unmanaged<Frame*, IntPtr, void> cb, IntPtr user_ptr, byte flags);
    [LibraryImport("uvc")] private static partial void uvc_stop_streaming(DeviceHandle devh);
    [LibraryImport("uvc")] private static partial void uvc_free_device_handle(DeviceHandle desc);

    public StreamCtrl GetStreamCtrlFormatSize(FrameFormat format, int width, int height, int fps) {
        uvc_get_stream_ctrl_format_size(this, out StreamCtrl result, format, width, height, fps).ThrowIfError();
        return result;
    }

    [UnmanagedCallersOnly]
    static unsafe void OnFrame(Frame* frame, System.IntPtr userptr) =>
        ((FrameCallback)GCHandle.FromIntPtr(userptr).Target!)(in *frame);

    public GCHandle StartStreaming(in StreamCtrl ctrl, FrameCallback cb) {
        StreamCtrl control = ctrl;
        GCHandle onFrame = GCHandle.Alloc(cb);
        try {
            unsafe {
                uvc_start_streaming(this, ref control, &OnFrame, GCHandle.ToIntPtr(onFrame), 0).ThrowIfError();
            }
        } catch(Exception) {
            onFrame.Free();
            throw;
        }
        return onFrame;
    }

    public void StopStreaming(GCHandle onFrame) {
        uvc_stop_streaming(this);
        onFrame.Free();
    }

    public void Dispose() {
        if(this._native == IntPtr.Zero)
            return;
        uvc_free_device_handle(this);
        this = default;
    }
}

[StructLayout(LayoutKind.Sequential)]
partial struct Device : IDisposable {
    IntPtr _native; // struct uvc_device*

    [StructLayout(LayoutKind.Sequential)]
    struct DeviceDescriptor { // struct uvc_device_descriptor
        public ushort idVendor, idProduct, bcdUVC;
        public unsafe byte* serialNumber, manufacturer, product;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe partial struct Descriptor : IDisposable {
        DeviceDescriptor* _native;

        [LibraryImport("uvc")] private static partial void uvc_free_device_descriptor(Descriptor desc);

        public ushort idVendor => this._native->idVendor;
        public ushort idProduct => this._native->idProduct;
        public ushort bcdUVC => this._native->bcdUVC;
        public ReadOnlySpan<byte> serialNumber => MemoryMarshal.CreateReadOnlySpanFromNullTerminated(this._native->serialNumber);
        public ReadOnlySpan<byte> manufacturer => MemoryMarshal.CreateReadOnlySpanFromNullTerminated(this._native->manufacturer);
        public ReadOnlySpan<byte> product => MemoryMarshal.CreateReadOnlySpanFromNullTerminated(this._native->product);

        public void Dispose() {
            if(this._native == null)
                return;
            uvc_free_device_descriptor(this);
            this = default;
        }
    }

    [LibraryImport("uvc")] private static partial Error uvc_get_device_descriptor(Device dev, out Descriptor desc);
    [LibraryImport("uvc")] private static partial Error uvc_open(Device dev, out DeviceHandle desc);
    [LibraryImport("uvc")] private static partial void uvc_ref_device(Device dev);
    [LibraryImport("uvc")] private static partial void uvc_unref_device(Device dev);

    public bool IsNull() => this._native == IntPtr.Zero;

    public Descriptor GetDescriptor() {
        uvc_get_device_descriptor(this, out Descriptor result).ThrowIfError();
        return result;
    }

    public DeviceHandle Open() {
        uvc_open(this, out DeviceHandle result).ThrowIfError();
        return result;
    }

    public Device Ref() {
        uvc_ref_device(this);
        return this;
    }

    public void Dispose() {
        if(this._native == IntPtr.Zero)
            return;
        uvc_unref_device(this);
        this = default;
    }
}

[StructLayout(LayoutKind.Sequential)]
unsafe partial struct DeviceList : IDisposable { // struct uvc_device**
    Device* _devices;

    public ref struct Enumerator {
        DeviceList _current;

        public Enumerator(DeviceList list) =>
            this._current = list;

        public Device Current => *this._current._devices;

        public bool MoveNext() {
            if(this._current._devices->IsNull())
                return false;
            return !(++this._current._devices)->IsNull();
        }
    }

    [LibraryImport("uvc")] private static partial void uvc_free_device_list(DeviceList list, byte unref_devices);

    public Enumerator GetEnumerator() => new(this);

    public void Dispose() {
        if(this._devices == null)
            return;
        uvc_free_device_list(this, 1);
        this = default;
    }
}

[StructLayout(LayoutKind.Sequential)]
partial struct Context : IDisposable { // struct uvc_context*
    IntPtr _native;

    [LibraryImport("uvc")] private static partial Error uvc_init(out Context ctx, System.IntPtr usb_ctx);
    [LibraryImport("uvc")] private static partial void uvc_exit(Context ctx);
    [LibraryImport("uvc")] private static partial Error uvc_get_device_list(Context ctx, out DeviceList list);

    public Context() =>
        uvc_init(out this, IntPtr.Zero).ThrowIfError();

    public DeviceList GetDeviceList() {
        uvc_get_device_list(this, out DeviceList result).ThrowIfError();
        return result;
    }

    public void Dispose() {
        if(this._native == IntPtr.Zero)
            return;
        uvc_exit(this);
        this = new();
    }
}
