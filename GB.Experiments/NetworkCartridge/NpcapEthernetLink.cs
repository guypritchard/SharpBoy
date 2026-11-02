using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GB.Experiments.NetworkCartridge;

public sealed record EthernetAdapter(string Name, string Description);

/// <summary>Optional installed Npcap driver. Never installs a driver or modifies adapter configuration.</summary>
public sealed class NpcapEthernetLink : IEthernetLink
{
    private readonly CaptureHandle handle;
    private bool disposed;
    private static string LibraryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Npcap", "wpcap.dll");
    public static bool IsAvailable => OperatingSystem.IsWindows() && File.Exists(LibraryPath);

    public static IReadOnlyList<EthernetAdapter> ListAdapters()
    {
        EnsureAvailable();
        var error = new System.Text.StringBuilder(256);
        if (Native.pcap_findalldevs(out IntPtr first, error) != 0) throw new IOException(error.ToString());
        try
        {
            var result = new List<EthernetAdapter>();
            for (IntPtr next = first; next != IntPtr.Zero;)
            {
                var item = Marshal.PtrToStructure<Adapter>(next);
                if (item.Name != IntPtr.Zero && (item.Flags & 1) == 0)
                    result.Add(new(Marshal.PtrToStringAnsi(item.Name)!, Marshal.PtrToStringAnsi(item.Description) ?? "Ethernet adapter"));
                next = item.Next;
            }
            return result;
        }
        finally { Native.pcap_freealldevs(first); }
    }
    public NpcapEthernetLink(string adapter)
    {
        EnsureAvailable();
        if (!ListAdapters().Any(item => item.Name == adapter)) throw new ArgumentException("Select an exact adapter name from --list-adapters.", nameof(adapter));
        var error = new System.Text.StringBuilder(256);
        this.handle = new CaptureHandle(Native.pcap_open_live(adapter, 65536, 1, 50, error));
        if (this.handle.IsInvalid) { this.handle.Dispose(); throw new IOException(error.ToString()); }
        try
        {
            if (Native.pcap_datalink(this.handle) != 1) throw new IOException("The selected adapter must expose Ethernet II frames.");
            if (Native.pcap_setnonblock(this.handle, 1, error) != 0) throw new IOException(error.ToString());
            if (Native.pcap_compile(this.handle, out Filter filter, "udp port 68 or (vlan and udp port 68)", 1, uint.MaxValue) != 0)
                throw this.Error();
            try { if (Native.pcap_setfilter(this.handle, ref filter) != 0) throw this.Error(); }
            finally { Native.pcap_freecode(ref filter); }
        }
        catch { this.handle.Dispose(); throw; }
    }
    public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this); token.ThrowIfCancellationRequested();
        byte[] bytes = frame.ToArray();
        await Task.Run(() => { if (Native.pcap_sendpacket(this.handle, bytes, bytes.Length) != 0) throw this.Error(); }, token);
    }
    public async ValueTask<byte[]> ReceiveAsync(CancellationToken token)
    {
        while (true)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this); token.ThrowIfCancellationRequested();
            int status = Native.pcap_next_ex(this.handle, out IntPtr header, out IntPtr data);
            if (status == 1)
            {
                // On Windows, pcap_pkthdr is two 32-bit timeval fields followed by caplen and len.
                uint count = unchecked((uint)Marshal.ReadInt32(header, 8));
                if (count > 65536) throw new IOException("Invalid capture length.");
                var bytes = new byte[count]; Marshal.Copy(data, bytes, 0, bytes.Length); return bytes;
            }
            if (status < 0) throw this.Error();
            await Task.Delay(2, token);
        }
    }
    private IOException Error() => new(Marshal.PtrToStringAnsi(Native.pcap_geterr(this.handle)) ?? "Npcap operation failed.");
    private static void EnsureAvailable()
    { if (!IsAvailable) throw new PlatformNotSupportedException("Npcap is not installed for this Windows process. Install Npcap separately to enable an Ethernet adapter; the virtual demo remains available."); }
    public ValueTask DisposeAsync() { if (!this.disposed) { this.disposed = true; this.handle.Dispose(); } return ValueTask.CompletedTask; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Adapter { public IntPtr Next, Name, Description, Addresses; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Filter { public uint Length; public IntPtr Instructions; }
    private sealed class CaptureHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public CaptureHandle(IntPtr value) : base(true) => this.SetHandle(value);
        protected override bool ReleaseHandle() { Native.pcap_close(this.handle); return true; }
    }
    private static class Native
    {
        static Native() => NativeLibrary.SetDllImportResolver(typeof(NpcapEthernetLink).Assembly, Resolve);
        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? paths) => name == "wpcap"
            ? NativeLibrary.Load(LibraryPath, assembly, DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32) : IntPtr.Zero;
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern int pcap_findalldevs(out IntPtr devices, System.Text.StringBuilder error);
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)] internal static extern void pcap_freealldevs(IntPtr devices);
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern IntPtr pcap_open_live(string adapter, int snaplen, int promiscuous, int timeout, System.Text.StringBuilder error);
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)] internal static extern void pcap_close(IntPtr handle);
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_datalink(CaptureHandle handle);
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern int pcap_setnonblock(CaptureHandle handle, int mode, System.Text.StringBuilder error);
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern int pcap_compile(CaptureHandle handle, out Filter filter, string expression, int optimize, uint mask);
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_setfilter(CaptureHandle handle, ref Filter filter);
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)] internal static extern void pcap_freecode(ref Filter filter);
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_next_ex(CaptureHandle handle, out IntPtr header, out IntPtr data);
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_sendpacket(CaptureHandle handle, byte[] packet, int length);
        [DllImport("wpcap", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr pcap_geterr(CaptureHandle handle);
    }
}
