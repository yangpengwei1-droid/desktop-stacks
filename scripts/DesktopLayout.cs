using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[ComImport,Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IStackServiceProvider { [PreserveSig]int QueryService(ref Guid service,ref Guid iid,[MarshalAs(UnmanagedType.IUnknown)]out object result); }
[ComImport,Guid("000214E2-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IStackShellBrowser {
 void GetWindow();void ContextSensitiveHelp();void InsertMenus();void SetMenu();void RemoveMenus();void SetStatusText();void EnableModeless();void TranslateAccelerator();void BrowseObject();void GetViewStateStream();void GetControlWindow();void SendControlMsg();
 void QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)]out object view);
}
[ComImport,Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IStackFolderView {
 void GetCurrentViewMode();void SetCurrentViewMode();void GetFolder();void Item(int index,out IntPtr item);void ItemCount(uint flags,out int count);
 void Items();void GetSelectionMarkedItem();void GetFocusedItem();void GetItemPosition(IntPtr item,out DesktopLayoutReader.Point position);
}
public class DesktopLayoutItem { public string source,name,group;public int x,y; }
public static class DesktopLayoutReader {
 [StructLayout(LayoutKind.Sequential)]public struct Point {public int X,Y;}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHGetNameFromIDList(IntPtr item,uint kind,out IntPtr name);
 static string Name(IntPtr item,uint kind){IntPtr name;Marshal.ThrowExceptionForHR(SHGetNameFromIDList(item,kind,out name));try{return Marshal.PtrToStringUni(name);}finally{Marshal.FreeCoTaskMem(name);}}
 public static DesktopLayoutItem[] Read(object desktop,int desktopWidth) {
  var services=(IStackServiceProvider)desktop;Guid service=new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"),iid=typeof(IStackShellBrowser).GUID;object browser;
  Marshal.ThrowExceptionForHR(services.QueryService(ref service,ref iid,out browser));object shellView;((IStackShellBrowser)browser).QueryActiveShellView(out shellView);
  var view=(IStackFolderView)shellView;int count;view.ItemCount(2,out count);var result=new List<DesktopLayoutItem>();
  for(int index=0;index<count;index++){IntPtr item;view.Item(index,out item);try{Point point;view.GetItemPosition(item,out point);result.Add(new DesktopLayoutItem{source=Name(item,0x80028000),name=Name(item,0),x=point.X,y=point.Y,group=point.X<desktopWidth/3?"左边栏":point.X<desktopWidth*2/3?"中间栏":"右边栏"});}finally{Marshal.FreeCoTaskMem(item);}}
  return result.ToArray();
 }
}
