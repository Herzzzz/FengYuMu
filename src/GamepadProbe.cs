using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MapleOverlay
{
    // XInput exposes a fixed 16-bit button mask, so controller back buttons / paddles
    // (Flydigi Apex 3 M1-M4, Xbox Elite P1-P4) can never appear there. Whether they are
    // readable at all depends on the firmware: a pad that reports them as real HID
    // buttons can be used directly, while one that maps them onto A/B/X/Y internally
    // cannot be distinguished from those buttons by any API.
    //
    // This probe enumerates every HID game controller and prints the button usages that
    // actually change, so the question is settled by measurement instead of assumption.
    // Reads are overlapped with a short timeout because a synchronous ReadFile on a HID
    // handle blocks until the device sends something.
    internal static class GamepadProbe
    {
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;
        private const uint GenericRead = 0x80000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileFlagOverlapped = 0x40000000;
        private const int HidpInput = 0;
        private const int HidpStatusSuccess = 0x00110000;
        private const int HidpStatusBufferTooSmall = 0x00110003;
        private const int ErrorIoPending = 997;
        private const uint WaitObject0 = 0;
        private const uint ReadTimeoutMs = 8;

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidAttributes
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidCapabilities
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidpButtonCaps
        {
            public ushort UsagePage;
            public byte ReportID;
            public byte IsAlias;
            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;
            public byte IsRange;
            public byte IsStringRange;
            public byte IsDesignatorRange;
            public byte IsAbsolute;
            public byte HasNull;
            public byte Reserved;
            public ushort BitSize;
            public ushort ReportCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public ushort[] Reserved2;
            public ushort UsageMin;
            public ushort UsageMax;
            public ushort StringMin;
            public ushort StringMax;
            public ushort DesignatorMin;
            public ushort DesignatorMax;
            public ushort DataIndexMin;
            public ushort DataIndexMax;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeOverlapped
        {
            public IntPtr InternalLow;
            public IntPtr InternalHigh;
            public int OffsetLow;
            public int OffsetHigh;
            public IntPtr EventHandle;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);
        [DllImport("hid.dll")]
        private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
        [DllImport("hid.dll")]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsedData);
        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HidCapabilities capabilities);
        [DllImport("hid.dll")]
        private static extern int HidP_GetButtonCaps(int reportType, [Out] HidpButtonCaps[] buttonCaps,
            ref ushort buttonCapsLength, IntPtr preparsedData);
        [DllImport("hid.dll")]
        private static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
            [Out] ushort[] usageList, ref int usageLength, IntPtr preparsedData,
            byte[] report, int reportLength);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator,
            IntPtr hwndParent, uint flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
            ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData,
            int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess,
            uint shareMode, IntPtr securityAttributes, uint creationDisposition,
            uint flagsAndAttributes, IntPtr templateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset,
            bool initialState, IntPtr name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(SafeHandle handle,
            ref NativeOverlapped overlapped, out uint bytesTransferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeHandle handle, byte[] buffer, uint bytesToRead,
            IntPtr bytesRead, ref NativeOverlapped overlapped);

        private sealed class ProbeDevice : IDisposable
        {
            public string Path;
            public ushort VendorId;
            public ushort ProductId;
            public int ButtonCount;
            public int ReportLength;
            public SafeFileHandle Handle;
            public IntPtr Preparsed;
            public HidpButtonCaps[] ButtonCaps;
            public int MaxUsages = 1;
            public IntPtr ReadEvent;

            public void Dispose()
            {
                if (ReadEvent != IntPtr.Zero) { CloseHandle(ReadEvent); ReadEvent = IntPtr.Zero; }
                if (Preparsed != IntPtr.Zero) { HidD_FreePreparsedData(Preparsed); Preparsed = IntPtr.Zero; }
                if (Handle != null) { Handle.Dispose(); Handle = null; }
            }
        }

        private static string DescribeUsage(ushort usage)
        {
            switch (usage)
            {
                case 1: return "常见对应 A / 下";
                case 2: return "常见对应 B / 右";
                case 3: return "常见对应 X / 左";
                case 4: return "常见对应 Y / 上";
                case 5: return "常见对应 LB";
                case 6: return "常见对应 RB";
                case 7: return "常见对应 LT";
                case 8: return "常见对应 RT";
                case 9: return "常见对应 Back / Select";
                case 10: return "常见对应 Start";
                case 11: return "常见对应 L3";
                case 12: return "常见对应 R3";
                case 13: return "常见对应 Guide";
                case 14: return "常见对应 Share";
                default: return "非标准按键（很可能就是背键）";
            }
        }

        private static string ToHex(byte[] report)
        {
            if (report == null) return "";
            StringBuilder hex = new StringBuilder(report.Length * 3);
            for (int i = 0; i < report.Length; i++)
            {
                if (i > 0) hex.Append(' ');
                hex.Append(report[i].ToString("X2"));
            }
            return hex.ToString();
        }

        private static List<ProbeDevice> Enumerate()
        {
            List<ProbeDevice> found = new List<ProbeDevice>();
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                DigcfPresent | DigcfDeviceInterface);
            if (set == new IntPtr(-1)) return found;
            try
            {
                uint index = 0;
                while (true)
                {
                    SpDeviceInterfaceData data = new SpDeviceInterfaceData();
                    data.cbSize = Marshal.SizeOf(typeof(SpDeviceInterfaceData));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data)) break;
                    index++;
                    int required;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        // cbSize is the header size only (not the variable-length buffer).
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required,
                            out required, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringAuto(new IntPtr(detail.ToInt64() + 4));
                        if (String.IsNullOrEmpty(path)) continue;
                        ProbeDevice device = TryOpen(path);
                        if (device != null) found.Add(device);
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return found;
        }

        private static ProbeDevice TryOpen(string path)
        {
            SafeFileHandle handle = CreateFile(path, GenericRead,
                FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting,
                FileFlagOverlapped, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); return null; }
            IntPtr preparsed;
            if (!HidD_GetPreparsedData(handle, out preparsed)) { handle.Dispose(); return null; }
            try
            {
                HidCapabilities caps;
                if (HidP_GetCaps(preparsed, out caps) != HidpStatusSuccess) return null;
                // 0x01 = Generic Desktop; 0x04 Joystick, 0x05 Game Pad, 0x08 Multi-axis.
                bool isGameController = caps.UsagePage == 0x01 &&
                    (caps.Usage == 0x04 || caps.Usage == 0x05 || caps.Usage == 0x08);
                if (!isGameController) return null;
                if (caps.InputReportByteLength == 0 || caps.NumberInputButtonCaps == 0) return null;

                ProbeDevice device = new ProbeDevice();
                device.Path = path;
                device.Handle = handle;
                device.Preparsed = preparsed;
                device.ButtonCount = caps.NumberInputButtonCaps;
                device.ReportLength = caps.InputReportByteLength;
                HidAttributes attributes = new HidAttributes();
                attributes.Size = Marshal.SizeOf(typeof(HidAttributes));
                if (HidD_GetAttributes(handle, ref attributes))
                {
                    device.VendorId = attributes.VendorID;
                    device.ProductId = attributes.ProductID;
                }
                HidpButtonCaps[] buttonCaps = new HidpButtonCaps[caps.NumberInputButtonCaps];
                ushort capsLength = caps.NumberInputButtonCaps;
                if (HidP_GetButtonCaps(HidpInput, buttonCaps, ref capsLength, preparsed) != HidpStatusSuccess)
                    return null;
                device.ButtonCaps = buttonCaps;
                int maxUsages = 0;
                for (int i = 0; i < capsLength; i++)
                    maxUsages += buttonCaps[i].ReportCount > 0 ? buttonCaps[i].ReportCount : 1;
                device.MaxUsages = Math.Max(1, maxUsages);
                device.ReadEvent = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                // Ownership of handle + preparsed moved into the device.
                handle = null;
                preparsed = IntPtr.Zero;
                return device;
            }
            finally
            {
                if (preparsed != IntPtr.Zero) HidD_FreePreparsedData(preparsed);
                if (handle != null) handle.Dispose();
            }
        }

        private static List<ushort> ReadPressedButtons(ProbeDevice device, out byte[] rawReport, out bool readOk)
        {
            rawReport = null;
            readOk = false;
            List<ushort> pressed = new List<ushort>();
            if (device.Handle == null || device.ButtonCaps == null) return pressed;
            byte[] report = new byte[device.ReportLength];
            NativeOverlapped overlapped = new NativeOverlapped();
            overlapped.EventHandle = device.ReadEvent;
            if (device.ReadEvent != IntPtr.Zero) WaitForSingleObject(device.ReadEvent, 0);
            bool started = ReadFile(device.Handle, report, (uint)report.Length, IntPtr.Zero, ref overlapped);
            if (!started)
            {
                if (Marshal.GetLastWin32Error() != ErrorIoPending) return pressed;
                if (WaitForSingleObject(device.ReadEvent, ReadTimeoutMs) != WaitObject0) return pressed;
                uint transferred;
                if (!GetOverlappedResult(device.Handle, ref overlapped, out transferred, false)) return pressed;
            }
            readOk = true;
            rawReport = report;
            ushort[] usages = new ushort[device.MaxUsages];
            foreach (HidpButtonCaps cap in device.ButtonCaps)
            {
                int usageLength = device.MaxUsages;
                // The third parameter is the link collection index, NOT the report id.
                // Passing the report id here silently returns no usages at all.
                int status = HidP_GetUsages(HidpInput, cap.UsagePage, cap.LinkCollection,
                    usages, ref usageLength, device.Preparsed, report, report.Length);
                if (status != HidpStatusSuccess) continue;
                for (int i = 0; i < usageLength; i++) pressed.Add(usages[i]);
            }
            return pressed;
        }

        public static string Run(int seconds)
        {
            StringBuilder log = new StringBuilder();
            try
            {
                RunCore(seconds, log);
            }
            catch (Exception ex)
            {
                log.AppendLine();
                log.AppendLine("探测过程中出错：" + ex);
            }
            return log.ToString();
        }

        private static void RunCore(int seconds, StringBuilder log)
        {
            log.AppendLine("枫语幕手柄背键探测");
            log.AppendLine("本工具绕过 XInput，直接读取手柄的 HID 原始报告。");
            log.AppendLine("采集期间请逐个按下背键（八爪鱼3 的 M1-M4）。");
            log.AppendLine("出现“新按钮”并且标注为“非标准按键”，说明背键可独立识别、可直接绑定；");
            log.AppendLine("按背键却没有任何“新按钮”，说明手柄把它们映射成了别的键。");
            log.AppendLine();

            List<ProbeDevice> devices = Enumerate();
            try
            {
                if (devices.Count == 0)
                {
                    log.AppendLine("没有找到 HID 游戏控制器。请确认手柄已连接，并拨到 XInput 档后重试。");
                    return;
                }
                log.AppendLine("发现 " + devices.Count + " 个 HID 游戏控制器：");
                foreach (ProbeDevice device in devices)
                {
                    log.AppendLine(String.Format("  VID_{0:X4} PID_{1:X4}  按钮集合={2}  报告长度={3}",
                        device.VendorId, device.ProductId, device.ButtonCount, device.ReportLength));
                }
                log.AppendLine();
                log.AppendLine("开始采集 " + seconds + " 秒……（现在请按背键）");

                Dictionary<ProbeDevice, HashSet<ushort>> known = new Dictionary<ProbeDevice, HashSet<ushort>>();
                Dictionary<ProbeDevice, byte[]> lastReport = new Dictionary<ProbeDevice, byte[]>();
                foreach (ProbeDevice device in devices) known[device] = new HashSet<ushort>();

                int readAttempts = 0, readSuccess = 0;
                DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                while (DateTime.UtcNow < deadline)
                {
                    foreach (ProbeDevice device in devices)
                    {
                        byte[] report;
                        bool readOk;
                        List<ushort> pressed = ReadPressedButtons(device, out report, out readOk);
                        readAttempts++;
                        if (readOk) readSuccess++;
                        if (readOk && report != null)
                        {
                            byte[] previous;
                            if (!lastReport.TryGetValue(device, out previous))
                            {
                                lastReport[device] = report;
                                log.AppendLine(String.Format("  [{0:X4}:{1:X4}] 初始报告: {2}",
                                    device.VendorId, device.ProductId, ToHex(report)));
                            }
                            else
                            {
                                // Diffing the raw report is the ground truth: it shows exactly
                                // which byte/bit the pad actually toggles for a back button,
                                // regardless of how the firmware labels it.
                                for (int bi = 0; bi < report.Length && bi < previous.Length; bi++)
                                {
                                    byte diff = (byte)(previous[bi] ^ report[bi]);
                                    if (diff == 0) continue;
                                    for (int bit = 0; bit < 8; bit++)
                                    {
                                        if ((diff & (1 << bit)) == 0) continue;
                                        bool down = (report[bi] & (1 << bit)) != 0;
                                        log.AppendLine(String.Format(
                                            "  [{0:X4}:{1:X4}] 字节{2} 位{3} {4}   （报告: {5}）",
                                            device.VendorId, device.ProductId, bi, bit,
                                            down ? "按下" : "松开", ToHex(report)));
                                    }
                                }
                                lastReport[device] = report;
                            }
                        }
                        foreach (ushort usage in pressed)
                        {
                            if (!known[device].Add(usage)) continue;
                            log.AppendLine(String.Format("  [{0:X4}:{1:X4}] 新按钮 usage={2}  （{3}）",
                                device.VendorId, device.ProductId, usage, DescribeUsage(usage)));
                        }
                    }
                    Thread.Sleep(4);
                }

                log.AppendLine();
                log.AppendLine(String.Format("读取尝试 {0} 次，成功 {1} 次。", readAttempts, readSuccess));
                if (readSuccess == 0)
                {
                    log.AppendLine("一次都没读到报告：手柄可能没有向这个接口发送数据，");
                    log.AppendLine("或该接口被其它程序（例如 Steam / 飞智空间站）独占。");
                }

                log.AppendLine();
                log.AppendLine("采集结束。各设备本次见到的按钮编号：");
                foreach (ProbeDevice device in devices)
                {
                    List<ushort> seen = new List<ushort>(known[device]);
                    seen.Sort();
                    StringBuilder line = new StringBuilder();
                    foreach (ushort usage in seen)
                    {
                        if (line.Length > 0) line.Append(", ");
                        line.Append(usage);
                    }
                    log.AppendLine(String.Format("  VID_{0:X4} PID_{1:X4}: {2}",
                        device.VendorId, device.ProductId,
                        line.Length == 0 ? "（没读到任何按钮）" : line.ToString()));
                }
            }
            finally
            {
                foreach (ProbeDevice device in devices) device.Dispose();
            }
        }
    }
}
