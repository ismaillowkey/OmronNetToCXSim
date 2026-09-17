using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetToCXSim.Services
{
    public class OmronSimulatorEngine : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int PROCESS_VM_READ = 0x0010;
        private const int PROCESS_VM_WRITE = 0x0020;
        private const int PROCESS_VM_OPERATION = 0x0008;

        private IntPtr _hProcess = IntPtr.Zero;
        private int _processId = 0;
        private string _processPath = "";

        // The actual live memory base pointer of CxCpuMain
        private IntPtr _mainMemoryBase = IntPtr.Zero;
        private IntPtr _cxMiscBase = IntPtr.Zero;

        // Omron internal area offsets from main memory base (verified live from CX-Simulator engine)
        private const int TIM_PV_OFFSET   = 0x1C000; // Timer Present Value (4096 words, T0-T4095)
        private const int CNT_PV_OFFSET   = 0x1E000; // Counter Present Value (4096 words, C0-C4095)
        private const int TIM_FLAG_OFFSET = 0x17C00; // Timer Completion Flags (4096 bits)
        private const int CNT_FLAG_OFFSET = 0x17E00; // Counter Completion Flags (4096 bits)
        private const int CIO_OFFSET      = 0x18000;
        private const int HR_OFFSET       = 0x1B800;
        private const int WR_OFFSET       = 0x1BC00;
        private const int AR_OFFSET       = 0x17000;
        private const int DM_OFFSET       = 0x20000;
        private const int EM_OFFSET       = 0x30000;

        public bool IsConnected => _hProcess != IntPtr.Zero && _mainMemoryBase != IntPtr.Zero;
        public int ProcessId => _processId;
        public string ProcessPath => _processPath;

        public bool Attach()
        {
            try
            {
                var procs = Process.GetProcessesByName("CxCpuMain");
                if (procs.Length == 0)
                {
                    Detach();
                    return false;
                }

                var proc = procs[0];
                if (_hProcess != IntPtr.Zero && _processId == proc.Id && _mainMemoryBase != IntPtr.Zero)
                {
                    return true;
                }

                Detach();

                _processId = proc.Id;
                _processPath = "CXCPUMain.exe";

                _hProcess = OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION, false, _processId);
                if (_hProcess == IntPtr.Zero) return false;

                // Find CxCpuMisc.dll module base address
                foreach (ProcessModule m in proc.Modules)
                {
                    if (m.ModuleName.Equals("CxCpuMisc.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        _cxMiscBase = m.BaseAddress;
                        break;
                    }
                }

                if (_cxMiscBase == IntPtr.Zero) return false;

                // Resolve global pointer: ds:[_cxMiscBase + 0xA184]
                byte[] ptrBuf = new byte[4];
                IntPtr globalPtrAddr = (IntPtr)((long)_cxMiscBase + 0xA184);
                if (!ReadProcessMemory(_hProcess, globalPtrAddr, ptrBuf, 4, out _)) return false;

                uint p1 = BitConverter.ToUInt32(ptrBuf, 0);
                if (p1 == 0) return false;

                if (!ReadProcessMemory(_hProcess, (IntPtr)p1, ptrBuf, 4, out _)) return false;

                uint pBase = BitConverter.ToUInt32(ptrBuf, 0);
                if (pBase == 0) return false;

                _mainMemoryBase = (IntPtr)pBase;
                return true;
            }
            catch
            {
                Detach();
                return false;
            }
        }

        public void Detach()
        {
            if (_hProcess != IntPtr.Zero)
            {
                CloseHandle(_hProcess);
                _hProcess = IntPtr.Zero;
            }
            _processId = 0;
            _mainMemoryBase = IntPtr.Zero;
            _cxMiscBase = IntPtr.Zero;
        }

        /// <summary>
        /// Reads a bit from live simulator memory.
        /// Supports: "100.0", "100.00", "0.0", "CIO100.0", "W0.0", "H0.0", "D0.0", "T0", "TIM0", "C0", "CNT0"
        /// </summary>
        public bool? ReadBit(string address)
        {
            if (!IsConnected && !Attach()) return null;

            if (TryParseAddress(address, out MemoryArea area, out int word, out int bit))
            {
                if (area == MemoryArea.TIM_PV) area = MemoryArea.TIM_FLAG;
                else if (area == MemoryArea.CNT_PV) area = MemoryArea.CNT_FLAG;

                return ReadBit(area, word, bit);
            }
            return null;
        }

        /// <summary>
        /// Writes a bit to live simulator memory.
        /// </summary>
        public bool WriteBit(string address, bool value)
        {
            if (!IsConnected && !Attach()) return false;

            if (TryParseAddress(address, out MemoryArea area, out int word, out int bit))
            {
                if (area == MemoryArea.TIM_PV) area = MemoryArea.TIM_FLAG;
                else if (area == MemoryArea.CNT_PV) area = MemoryArea.CNT_FLAG;

                return WriteBit(area, word, bit, value);
            }
            return false;
        }

        public bool WriteBit(MemoryArea area, int word, int bit, bool value)
        {
            if (!IsConnected && !Attach()) return false;

            int areaOffset = GetAreaOffset(area);
            IntPtr targetAddr;
            int bitInByte;

            if (area == MemoryArea.TIM_FLAG || area == MemoryArea.CNT_FLAG)
            {
                int bitIndex = word;
                int byteOffset = bitIndex / 8;
                bitInByte = bitIndex % 8;
                targetAddr = (IntPtr)((long)_mainMemoryBase + areaOffset + byteOffset);
            }
            else
            {
                int byteOffset = word * 2;
                bitInByte = bit >= 8 ? (bit - 8) : bit;
                targetAddr = (IntPtr)((long)_mainMemoryBase + areaOffset + byteOffset + (bit >= 8 ? 1 : 0));
            }

            byte[] buf = new byte[1];
            if (ReadProcessMemory(_hProcess, targetAddr, buf, 1, out _))
            {
                if (value)
                    buf[0] |= (byte)(1 << bitInByte);
                else
                    buf[0] &= (byte)~(1 << bitInByte);

                return WriteProcessMemory(_hProcess, targetAddr, buf, 1, out _);
            }
            return false;
        }

        public ushort? ReadWord(MemoryArea area, int word)
        {
            if (!IsConnected && !Attach()) return null;

            int areaOffset = GetAreaOffset(area);
            int byteOffset = word * 2;
            IntPtr targetAddr = (IntPtr)((long)_mainMemoryBase + areaOffset + byteOffset);

            byte[] buf = new byte[2];
            if (ReadProcessMemory(_hProcess, targetAddr, buf, 2, out _))
            {
                return (ushort)(buf[0] | (buf[1] << 8));
            }
            return null;
        }

        public ushort? ReadWord(string address)
        {
            if (!IsConnected && !Attach()) return null;

            if (TryParseAddress(address, out MemoryArea area, out int word, out _))
            {
                return ReadWord(area, word);
            }
            return null;
        }

        public bool WriteWord(MemoryArea area, int word, ushort value)
        {
            if (!IsConnected && !Attach()) return false;

            int areaOffset = GetAreaOffset(area);
            int byteOffset = word * 2;
            IntPtr targetAddr = (IntPtr)((long)_mainMemoryBase + areaOffset + byteOffset);

            byte[] buf = new byte[2] { (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) };
            return WriteProcessMemory(_hProcess, targetAddr, buf, 2, out _);
        }

        /// <summary>
        /// Writes a 16-bit word to live simulator memory.
        /// </summary>
        public bool WriteWord(string address, ushort value)
        {
            if (!IsConnected && !Attach()) return false;

            if (TryParseAddress(address, out MemoryArea area, out int word, out _))
            {
                return WriteWord(area, word, value);
            }
            return false;
        }

        public bool? ReadBit(MemoryArea area, int word, int bit)
        {
            if (!IsConnected && !Attach()) return null;

            int areaOffset = GetAreaOffset(area);
            IntPtr targetAddr;
            int bitInByte;

            if (area == MemoryArea.TIM_FLAG || area == MemoryArea.CNT_FLAG)
            {
                int bitIndex = word;
                int byteOffset = bitIndex / 8;
                bitInByte = bitIndex % 8;
                targetAddr = (IntPtr)((long)_mainMemoryBase + areaOffset + byteOffset);
            }
            else
            {
                int byteOffset = word * 2;
                bitInByte = bit >= 8 ? (bit - 8) : bit;
                targetAddr = (IntPtr)((long)_mainMemoryBase + areaOffset + byteOffset + (bit >= 8 ? 1 : 0));
            }

            byte[] buf = new byte[1];
            if (ReadProcessMemory(_hProcess, targetAddr, buf, 1, out _))
            {
                return (buf[0] & (1 << bitInByte)) != 0;
            }
            return null;
        }

        public byte[] ReadBytes(MemoryArea area, int wordOffset, int byteCount)
        {
            if (!IsConnected && !Attach()) return null;

            int areaOffset = GetAreaOffset(area);
            int byteOffset = wordOffset * 2;
            IntPtr targetAddr = (IntPtr)((long)_mainMemoryBase + areaOffset + byteOffset);

            byte[] buf = new byte[byteCount];
            if (ReadProcessMemory(_hProcess, targetAddr, buf, byteCount, out _))
            {
                return buf;
            }
            return null;
        }

        public bool WriteBytes(MemoryArea area, int wordOffset, byte[] data)
        {
            if (!IsConnected && !Attach()) return false;
            if (data == null || data.Length == 0) return true;

            int areaOffset = GetAreaOffset(area);
            int byteOffset = wordOffset * 2;
            IntPtr targetAddr = (IntPtr)((long)_mainMemoryBase + areaOffset + byteOffset);

            return WriteProcessMemory(_hProcess, targetAddr, data, data.Length, out _);
        }

        public int GetAreaOffset(MemoryArea area)
        {
            switch (area)
            {
                case MemoryArea.CIO:
                    return CIO_OFFSET;      // 0x18000
                case MemoryArea.WR:
                    return WR_OFFSET;       // 0x1BC00
                case MemoryArea.HR:
                    return HR_OFFSET;       // 0x1B800
                case MemoryArea.AR:
                    return AR_OFFSET;       // 0x17000
                case MemoryArea.DM:
                    return DM_OFFSET;       // 0x20000
                case MemoryArea.EM:
                    return EM_OFFSET;       // 0x30000
                case MemoryArea.TIM_PV:
                    return TIM_PV_OFFSET;   // 0x1C000
                case MemoryArea.CNT_PV:
                    return CNT_PV_OFFSET;   // 0x1E000
                case MemoryArea.TIM_FLAG:
                    return TIM_FLAG_OFFSET; // 0x17C00
                case MemoryArea.CNT_FLAG:
                    return CNT_FLAG_OFFSET; // 0x17E00
                default:
                    return CIO_OFFSET;
            }
        }

        public enum MemoryArea
        {
            CIO,
            WR,
            HR,
            AR,
            DM,
            EM,
            TIM_PV,
            CNT_PV,
            TIM_FLAG,
            CNT_FLAG
        }

        public static bool TryParseAddress(string addrStr, out MemoryArea area, out int word, out int bit)
        {
            area = MemoryArea.CIO;
            word = 0;
            bit = 0;

            if (string.IsNullOrWhiteSpace(addrStr)) return false;
            addrStr = addrStr.Trim().ToUpperInvariant();

            if (addrStr.StartsWith("CIO"))
            {
                area = MemoryArea.CIO;
                addrStr = addrStr.Substring(3);
            }
            else if (addrStr.StartsWith("TIM"))
            {
                area = MemoryArea.TIM_PV;
                addrStr = addrStr.Substring(3);
            }
            else if (addrStr.StartsWith("CNT"))
            {
                area = MemoryArea.CNT_PV;
                addrStr = addrStr.Substring(3);
            }
            else if (addrStr.StartsWith("W"))
            {
                area = MemoryArea.WR;
                addrStr = addrStr.Substring(1);
            }
            else if (addrStr.StartsWith("H"))
            {
                area = MemoryArea.HR;
                addrStr = addrStr.Substring(1);
            }
            else if (addrStr.StartsWith("D"))
            {
                area = MemoryArea.DM;
                addrStr = addrStr.Substring(1);
            }
            else if (addrStr.StartsWith("T"))
            {
                area = MemoryArea.TIM_PV;
                addrStr = addrStr.Substring(1);
            }
            else if (addrStr.StartsWith("C"))
            {
                area = MemoryArea.CNT_PV;
                addrStr = addrStr.Substring(1);
            }
            else if (char.IsDigit(addrStr[0]))
            {
                area = MemoryArea.CIO;
            }

            if (addrStr.Contains("."))
            {
                var parts = addrStr.Split('.');
                if (int.TryParse(parts[0], out word) && int.TryParse(parts[1], out bit))
                {
                    return true;
                }
                return false;
            }
            else
            {
                if (int.TryParse(addrStr, out word))
                {
                    bit = 0;
                    return true;
                }
                return false;
            }
        }

        public void Dispose()
        {
            Detach();
        }
    }
}
