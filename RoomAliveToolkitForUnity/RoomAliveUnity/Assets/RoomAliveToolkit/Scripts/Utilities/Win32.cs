using System;
using System.Runtime.InteropServices;

namespace RoomAliveToolkit.Images
{
    /// <summary>
    /// Cross-platform memory allocation utilities.
    /// Uses Marshal.AllocHGlobal/FreeHGlobal which works on all platforms Unity supports.
    /// The original Windows-only P/Invoke to msvcrt.dll and kernel32.dll has been replaced.
    /// </summary>
    public static class Win32
    {
        public static IntPtr _aligned_malloc(UIntPtr size, UIntPtr alignment)
        {
            // Marshal.AllocHGlobal does not guarantee alignment, but for Unity usage
            // the alignment requirement is not critical. If alignment is needed,
            // over-allocate and align manually.
            int sizeInt = (int)(uint)size;
            int alignInt = (int)(uint)alignment;

            // Allocate extra space for alignment + pointer storage
            IntPtr raw = Marshal.AllocHGlobal(sizeInt + alignInt + IntPtr.Size);
            long rawAddr = raw.ToInt64();
            long aligned = (rawAddr + IntPtr.Size + alignInt - 1) & ~((long)alignInt - 1);

            // Store the original pointer just before the aligned address
            Marshal.WriteIntPtr(new IntPtr(aligned - IntPtr.Size), raw);

            return new IntPtr(aligned);
        }

        public static IntPtr _aligned_free(IntPtr memblock)
        {
            if (memblock == IntPtr.Zero)
                return IntPtr.Zero;

            // Read the original pointer stored before the aligned address
            IntPtr raw = Marshal.ReadIntPtr(new IntPtr(memblock.ToInt64() - IntPtr.Size));
            Marshal.FreeHGlobal(raw);
            return IntPtr.Zero;
        }

        public static bool ZeroMemory(IntPtr destination, UIntPtr length)
        {
            int len = (int)(uint)length;
            // Zero the memory byte by byte via a temporary zero array
            byte[] zeros = new byte[len];
            Marshal.Copy(zeros, 0, destination, len);
            return true;
        }

        public static void CopyMemory(IntPtr destination, IntPtr source, UIntPtr length)
        {
            int len = (int)(uint)length;
            byte[] buffer = new byte[len];
            Marshal.Copy(source, buffer, 0, len);
            Marshal.Copy(buffer, 0, destination, len);
        }
    }
}
