using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ashla.Host.Gui;

[SupportedOSPlatform("windows")]
internal static unsafe partial class NativeFileDialog
{
    private const uint OFN_NOCHANGEDIR = 0x8, OFN_PATHMUSTEXIST = 0x800, OFN_FILEMUSTEXIST = 0x1000,
        OFN_EXPLORER = 0x80000, OFN_DONTADDTORECENT = 0x2000000;

    public static string? PickFile(nint owner, string title, string filterName, string filterPattern, string? initialFolder)
    {
        const int bufferLength = 1024;
        var filter = $"{filterName}\0{filterPattern}\0All files\0*.*\0\0";
        var buffer = new char[bufferLength];
        fixed (char* file = buffer)
        fixed (char* filterPtr = filter)
        fixed (char* titlePtr = title)
        fixed (char* folderPtr = initialFolder)
        {
            var dialog = new OpenFileName
            {
                StructSize = (uint)sizeof(OpenFileName),
                Owner = owner,
                Filter = filterPtr,
                FilterIndex = 1,
                File = file,
                MaxFile = bufferLength,
                InitialDir = Directory.Exists(initialFolder) ? folderPtr : null,
                Title = titlePtr,
                Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_DONTADDTORECENT
            };
            if (GetOpenFileNameW(ref dialog) == 0) return null;
            return new string(file);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileName
    {
        public uint StructSize;
        public nint Owner;
        public nint Instance;
        public char* Filter;
        public char* CustomFilter;
        public uint MaxCustomFilter;
        public uint FilterIndex;
        public char* File;
        public uint MaxFile;
        public char* FileTitle;
        public uint MaxFileTitle;
        public char* InitialDir;
        public char* Title;
        public uint Flags;
        public ushort FileOffset;
        public ushort FileExtension;
        public char* DefaultExtension;
        public nint CustomData;
        public nint Hook;
        public char* TemplateName;
        public nint Reserved;
        public uint ReservedNumber;
        public uint FlagsEx;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", SetLastError = true)]
    private static partial int GetOpenFileNameW(ref OpenFileName dialog);
}
