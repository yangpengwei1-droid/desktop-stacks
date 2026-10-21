using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace DesktopStacks {
 // Explicit Unicode interface; never convert desktop paths through the system ANSI code page.
 public static class NativeShortcut {
  [ComImport,Guid("00021401-0000-0000-C000-000000000046")]class ShellLinkObject {}
  [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IShellLinkW {
   void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder file,int capacity,IntPtr findData,uint flags);
   void GetIDList(out IntPtr pidl);void SetIDList(IntPtr pidl);
   void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int capacity);
   void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string text);
   void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder directory,int capacity);
   void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string directory);
   void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder arguments,int capacity);
   void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string arguments);
   void GetHotkey(out short key);void SetHotkey(short key);void GetShowCmd(out int command);void SetShowCmd(int command);
   void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder icon,int capacity,out int index);
   void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string icon,int index);
   void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string path,uint reserved);
   void Resolve(IntPtr window,uint flags);
   void SetPath([MarshalAs(UnmanagedType.LPWStr)]string path);
  }
  public static void Create(string destination,string target,string arguments,string workingDirectory,string icon) {
   if(!Path.IsPathRooted(destination)||!Path.IsPathRooted(target))throw new ArgumentException("Shortcut paths must be absolute file paths.");
   object instance=new ShellLinkObject();
   try {
    var link=(IShellLinkW)instance;link.SetPath(target);
    if(!string.IsNullOrEmpty(arguments))link.SetArguments(arguments);
    if(!string.IsNullOrEmpty(workingDirectory))link.SetWorkingDirectory(workingDirectory);
    if(!string.IsNullOrEmpty(icon))link.SetIconLocation(icon,0);
    ((IPersistFile)instance).Save(destination,true);
   }finally{Marshal.FinalReleaseComObject(instance);}
  }
  public static string ReadTarget(string path){return Read(path,false);}
  public static string ReadArguments(string path){return Read(path,true);}
  static string Read(string path,bool arguments) {
   object instance=new ShellLinkObject();
   try{((IPersistFile)instance).Load(path,0);var text=new StringBuilder(32768);if(arguments)((IShellLinkW)instance).GetArguments(text,text.Capacity);else ((IShellLinkW)instance).GetPath(text,text.Capacity,IntPtr.Zero,4);return text.ToString();}
   finally{Marshal.FinalReleaseComObject(instance);}
  }
 }
}
