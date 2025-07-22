using System;
using System.Runtime.InteropServices;

namespace DEMBuilder.Dialogs
{
    public class FolderSelectDialog
    {
        public string? InitialDirectory { get; set; }
        public string? Title { get; set; }
        public string? SelectedPath { get; private set; }

        public bool ShowDialog(IntPtr owner)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            if (!string.IsNullOrEmpty(InitialDirectory))
            {
                if (SHCreateItemFromParsingName(InitialDirectory, IntPtr.Zero, typeof(IShellItem).GUID, out var initialDirectoryItem) == 0)
                {
                    dialog.SetFolder(initialDirectoryItem);
                }
            }

            if (!string.IsNullOrEmpty(Title))
            {
                dialog.SetTitle(Title);
            }

            dialog.SetOkButtonLabel("Select Folder");
            dialog.SetFileName("Import Folder");
            dialog.SetFileNameLabel("Folder");
            dialog.SetOptions(FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PATHMUSTEXIST);

            if (dialog.Show(owner) == 0) // S_OK
            {
                dialog.GetResult(out var result);
                result.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);
                SelectedPath = System.IO.Path.GetDirectoryName(path);
                return !string.IsNullOrEmpty(SelectedPath);
            }

            return false;
        }

        #region P/Invoke

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, [In, MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IFileDialogEvents pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(FOS fos);
            void GetOptions(out FOS pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter([MarshalAs(UnmanagedType.Interface)] object pFilter);
        }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog : IFileDialog
        {
            void GetResults(out IShellItemArray ppenum);
            void GetSelectedItems(out IShellItemArray ppsai);
        }

        [ComImport, Guid("973510DB-7D7F-452B-8975-74A85828D354"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialogEvents { }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray 
        {
            void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void GetPropertyStore(int flags, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void GetPropertyDescriptionList([In] ref PROPERTYKEY keyType, [In] ref Guid riid, out IntPtr ppv);
            void GetAttributes(SIATTRIBFLAGS dwAttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
            void GetCount(out uint pdwNumItems);
            void GetItemAt(uint dwIndex, out IShellItem ppsi);
            void EnumItems(out IEnumShellItems ppenumShellItems);
        }
        
        [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumShellItems {}

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem ppv);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct COMDLG_FILTERSPEC { public string pszName; public string pszSpec; }
        
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PROPERTYKEY {
            public Guid fmtid;
            public uint pid;
        }
        
        private enum SIATTRIBFLAGS {
            SIATTRIBFLAGS_AND = 1,
            SIATTRIBFLAGS_OR = 2,
            SIATTRIBFLAGS_APPCOMPAT = 3
        }

        [Flags]
        private enum FOS : uint
        {
            FOS_OVERWRITEPROMPT = 0x00000002,
            FOS_STRICTFILETYPES = 0x00000004,
            FOS_NOCHANGEDIR = 0x00000008,
            FOS_PICKFOLDERS = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040,
            FOS_ALLNONSTORAGEITEMS = 0x00000080,
            FOS_NOVALIDATE = 0x00000100,
            FOS_ALLOWMULTISELECT = 0x00000200,
            FOS_PATHMUSTEXIST = 0x00000800,
            FOS_FILEMUSTEXIST = 0x00001000,
            FOS_CREATEPROMPT = 0x00002000,
            FOS_SHAREAWARE = 0x00004000,
            FOS_NOREADONLYRETURN = 0x00008000,
            FOS_NOTESTFILECREATE = 0x00010000,
            FOS_HIDEMRUPLACES = 0x00020000,
            FOS_HIDEPINNEDPLACES = 0x00040000,
            FOS_NODEREFERENCELINKS = 0x00100000,
            FOS_DONTADDTORECENT = 0x02000000,
            FOS_FORCESHOWHIDDEN = 0x10000000,
            FOS_DEFAULTNOMINIMODE = 0x20000000,
            FOS_FORCEPREVIEWPANEON = 0x40000000
        }

        private enum SIGDN : uint
        {
            SIGDN_DESKTOPABSOLUTEEDITING = 0x8004c000,
            SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000,
            SIGDN_FILESYSPATH = 0x80058000,
            SIGDN_NORMALDISPLAY = 0,
            SIGDN_PARENTRELATIVE = 0x80080001,
            SIGDN_PARENTRELATIVEEDITING = 0x80031001,
            SIGDN_PARENTRELATIVEFORADDRESSBAR = 0x8007c001,
            SIGDN_PARENTRELATIVEPARSING = 0x80018001,
            SIGDN_URL = 0x80068000
        }

        #endregion
    }
}
